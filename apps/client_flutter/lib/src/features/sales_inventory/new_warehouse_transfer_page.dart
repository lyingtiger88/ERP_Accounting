import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../accounting/report_support.dart';

class NewWarehouseTransferPage extends StatefulWidget {
  const NewWarehouseTransferPage({
    super.key,
    required this.accessToken,
  });

  final String accessToken;

  @override
  State<NewWarehouseTransferPage> createState() =>
      _NewWarehouseTransferPageState();
}

class _NewWarehouseTransferPageState
    extends State<NewWarehouseTransferPage> {
  final _apiClient = ApiClient();
  final _description = TextEditingController();
  final List<_TransferRow> _rows = [_TransferRow()];

  List<Map<String, dynamic>> _warehouses = const [];
  List<Map<String, dynamic>> _products = const [];
  String? _fromWarehouseId;
  String? _toWarehouseId;
  DateTime _date = DateTime.now();
  bool _loading = true;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _description.dispose();
    for (final row in _rows) {
      row.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final results = await Future.wait([
        _apiClient.getWarehouses(
          bearerToken: widget.accessToken,
        ),
        _apiClient.getStoreProducts(
          bearerToken: widget.accessToken,
        ),
      ]);

      if (!mounted) return;

      final warehouses =
          (results[0] as List<Map<String, dynamic>>)
              .where((x) => x['isActive'] as bool? ?? true)
              .toList(growable: false);

      final products =
          (results[1] as List<Map<String, dynamic>>)
              .where(
                (x) =>
                    (x['isActive'] as bool? ?? true) &&
                    (x['trackInventory'] as bool? ?? false),
              )
              .toList(growable: false);

      setState(() {
        _warehouses = warehouses;
        _products = products;
        _fromWarehouseId = warehouses.isEmpty
            ? null
            : warehouses.first['id'].toString();
        _toWarehouseId = warehouses.length < 2
            ? null
            : warehouses[1]['id'].toString();
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.toString();
        _loading = false;
      });
    }
  }

  Map<String, dynamic>? _product(String? id) {
    if (id == null) return null;
    for (final product in _products) {
      if (product['id'].toString() == id) return product;
    }
    return null;
  }

  num _number(String value) {
    return num.tryParse(value.replaceAll(',', '').trim()) ?? 0;
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );

    if (picked != null && mounted) {
      setState(() => _date = picked);
    }
  }

  Future<void> _pickExpiry(_TransferRow row) async {
    final picked = await showDatePicker(
      context: context,
      initialDate:
          row.expiryDate ?? DateTime.now().add(const Duration(days: 365)),
      firstDate: DateTime(2000),
      lastDate: DateTime(2200),
    );

    if (picked != null && mounted) {
      setState(() => row.expiryDate = picked);
    }
  }

  Future<void> _save() async {
    if (_fromWarehouseId == null || _toWarehouseId == null) {
      _message('برای انتقال حداقل دو انبار فعال لازم است.');
      return;
    }

    if (_fromWarehouseId == _toWarehouseId) {
      _message('انبار مبدا و مقصد باید متفاوت باشند.');
      return;
    }

    final lines = <Map<String, dynamic>>[];

    for (final row in _rows) {
      if (row.productId == null) continue;

      final quantity = _number(row.quantity.text);
      if (quantity <= 0) {
        _message('تعداد انتقال باید بزرگ‌تر از صفر باشد.');
        return;
      }

      final product = _product(row.productId);
      final tracking =
          product?['trackingMode']?.toString() ?? 'None';

      if (tracking == 'Lot' && row.lot.text.trim().isEmpty) {
        _message('برای کالای لات‌دار، شماره لات الزامی است.');
        return;
      }

      if (tracking == 'Serial') {
        if (row.serial.text.trim().isEmpty || quantity != 1) {
          _message('کالای سریالی باید با سریال و تعداد ۱ منتقل شود.');
          return;
        }
      }

      lines.add({
        'productId': row.productId,
        'quantity': quantity,
        'lotNumber': row.lot.text.trim().isEmpty
            ? null
            : row.lot.text.trim(),
        'serialNumber': row.serial.text.trim().isEmpty
            ? null
            : row.serial.text.trim(),
        'expiryDate': row.expiryDate == null
            ? null
            : _dateOnly(row.expiryDate!),
      });
    }

    if (lines.isEmpty) {
      _message('حداقل یک ردیف انتقال لازم است.');
      return;
    }

    setState(() => _saving = true);

    try {
      final result = await _apiClient.createWarehouseTransfer(
        bearerToken: widget.accessToken,
        documentDate: _date,
        fromWarehouseId: _fromWarehouseId!,
        toWarehouseId: _toWarehouseId!,
        description: _description.text.trim().isEmpty
            ? null
            : _description.text.trim(),
        lines: lines,
      );

      if (!mounted) return;
      Navigator.pop(context, result);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  String _dateOnly(DateTime value) {
    return value.year.toString().padLeft(4, '0') +
        '-' +
        value.month.toString().padLeft(2, '0') +
        '-' +
        value.day.toString().padLeft(2, '0');
  }

  void _message(String value) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(value)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(title: const Text('انتقال بین انبارها')),
        body: _loading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
                ? Center(child: Text(_error!))
                : ListView(
                    padding: const EdgeInsets.all(20),
                    children: [
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(18),
                          child: Wrap(
                            spacing: 12,
                            runSpacing: 12,
                            children: [
                              SizedBox(
                                width: 260,
                                child: DropdownButtonFormField<String>(
                                  initialValue: _fromWarehouseId,
                                  isExpanded: true,
                                  decoration: const InputDecoration(
                                    labelText: 'انبار مبدا',
                                  ),
                                  items: [
                                    for (final warehouse in _warehouses)
                                      DropdownMenuItem(
                                        value: warehouse['id'].toString(),
                                        child: Text(
                                          warehouse['code'].toString() +
                                              ' — ' +
                                              warehouse['name'].toString(),
                                        ),
                                      ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) => setState(
                                            () => _fromWarehouseId = value,
                                          ),
                                ),
                              ),
                              SizedBox(
                                width: 260,
                                child: DropdownButtonFormField<String>(
                                  initialValue: _toWarehouseId,
                                  isExpanded: true,
                                  decoration: const InputDecoration(
                                    labelText: 'انبار مقصد',
                                  ),
                                  items: [
                                    for (final warehouse in _warehouses)
                                      DropdownMenuItem(
                                        value: warehouse['id'].toString(),
                                        child: Text(
                                          warehouse['code'].toString() +
                                              ' — ' +
                                              warehouse['name'].toString(),
                                        ),
                                      ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) => setState(
                                            () => _toWarehouseId = value,
                                          ),
                                ),
                              ),
                              OutlinedButton.icon(
                                onPressed: _saving ? null : _pickDate,
                                icon: const Icon(
                                  Icons.calendar_month_outlined,
                                ),
                                label: Text(formatReportDate(_date)),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(18),
                          child: Column(
                            children: [
                              Row(
                                children: [
                                  const Expanded(
                                    child: Text(
                                      'اقلام انتقال',
                                      style: TextStyle(
                                        fontSize: 18,
                                        fontWeight: FontWeight.w700,
                                      ),
                                    ),
                                  ),
                                  TextButton.icon(
                                    onPressed: _saving
                                        ? null
                                        : () => setState(
                                              () => _rows.add(
                                                _TransferRow(),
                                              ),
                                            ),
                                    icon: const Icon(Icons.add),
                                    label: const Text('افزودن'),
                                  ),
                                ],
                              ),
                              for (var i = 0; i < _rows.length; i++) ...[
                                _TransferLineCard(
                                  row: _rows[i],
                                  products: _products,
                                  saving: _saving,
                                  onProductChanged: (value) =>
                                      setState(() {
                                    _rows[i].productId = value;
                                  }),
                                  onPickExpiry: () =>
                                      _pickExpiry(_rows[i]),
                                  onRemove: () {
                                    if (_rows.length <= 1) return;
                                    final removed = _rows.removeAt(i);
                                    removed.dispose();
                                    setState(() {});
                                  },
                                ),
                                const SizedBox(height: 10),
                              ],
                            ],
                          ),
                        ),
                      ),
                      TextField(
                        controller: _description,
                        maxLines: 2,
                        decoration: const InputDecoration(
                          labelText: 'شرح انتقال',
                        ),
                      ),
                      const SizedBox(height: 18),
                      FilledButton.icon(
                        onPressed: _saving ? null : _save,
                        icon: const Icon(Icons.save_outlined),
                        label: const Text('ذخیره انتقال به‌صورت پیش‌نویس'),
                      ),
                    ],
                  ),
      ),
    );
  }
}

class _TransferLineCard extends StatelessWidget {
  const _TransferLineCard({
    required this.row,
    required this.products,
    required this.saving,
    required this.onProductChanged,
    required this.onPickExpiry,
    required this.onRemove,
  });

  final _TransferRow row;
  final List<Map<String, dynamic>> products;
  final bool saving;
  final ValueChanged<String?> onProductChanged;
  final VoidCallback onPickExpiry;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    Map<String, dynamic>? product;
    for (final item in products) {
      if (item['id'].toString() == row.productId) {
        product = item;
        break;
      }
    }

    final tracking = product?['trackingMode']?.toString() ?? 'None';

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Wrap(
          spacing: 10,
          runSpacing: 10,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 300,
              child: DropdownButtonFormField<String>(
                initialValue: row.productId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'کالا',
                ),
                items: [
                  for (final p in products)
                    DropdownMenuItem(
                      value: p['id'].toString(),
                      child: Text(
                        p['sku'].toString() +
                            ' — ' +
                            p['name'].toString(),
                      ),
                    ),
                ],
                onChanged: saving ? null : onProductChanged,
              ),
            ),
            SizedBox(
              width: 120,
              child: TextField(
                controller: row.quantity,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(labelText: 'تعداد'),
              ),
            ),
            if (tracking == 'Lot')
              SizedBox(
                width: 160,
                child: TextField(
                  controller: row.lot,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(
                    labelText: 'شماره لات',
                  ),
                ),
              ),
            if (tracking == 'Serial')
              SizedBox(
                width: 180,
                child: TextField(
                  controller: row.serial,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(
                    labelText: 'شماره سریال',
                  ),
                ),
              ),
            if (tracking == 'Lot')
              OutlinedButton.icon(
                onPressed: saving ? null : onPickExpiry,
                icon: const Icon(Icons.event_outlined),
                label: Text(
                  row.expiryDate == null
                      ? 'انقضا'
                      : formatReportDate(row.expiryDate),
                ),
              ),
            IconButton(
              tooltip: 'حذف',
              onPressed: saving ? null : onRemove,
              icon: const Icon(Icons.delete_outline),
            ),
          ],
        ),
      ),
    );
  }
}

class _TransferRow {
  String? productId;
  final quantity = TextEditingController(text: '1');
  final lot = TextEditingController();
  final serial = TextEditingController();
  DateTime? expiryDate;

  void dispose() {
    quantity.dispose();
    lot.dispose();
    serial.dispose();
  }
}
