import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../accounting/report_support.dart';

class StockPage extends StatefulWidget {
  const StockPage({
    super.key,
    required this.accessToken,
  });

  final String accessToken;

  @override
  State<StockPage> createState() => _StockPageState();
}

class _StockPageState extends State<StockPage> {
  final _apiClient = ApiClient();

  List<Map<String, dynamic>> _warehouses = const [];
  List<Map<String, dynamic>> _products = const [];
  List<Map<String, dynamic>> _balances = const [];

  String? _warehouseId;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final results = await Future.wait([
        _apiClient.getWarehouses(
          bearerToken: widget.accessToken,
        ),
        _apiClient.getStoreProducts(
          bearerToken: widget.accessToken,
        ),
        _apiClient.getStockBalances(
          bearerToken: widget.accessToken,
          warehouseId: _warehouseId,
        ),
      ]);

      if (!mounted) return;

      setState(() {
        _warehouses = results[0];
        _products = results[1]
            .where(
              (item) =>
                  item['trackInventory'] as bool? ?? false,
            )
            .toList(growable: false);
        _balances = results[2];
      });
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _adjust() async {
    if (_warehouses.isEmpty || _products.isEmpty) {
      _message(
        'برای تعدیل موجودی، حداقل یک انبار و یک کالای موجودی‌پذیر لازم است.',
      );
      return;
    }

    var warehouseId = _warehouseId ?? _warehouses.first['id'].toString();
    var productId = _products.first['id'].toString();
    var date = DateTime.now();

    final quantity = TextEditingController();
    final unitCost = TextEditingController();
    final reason = TextEditingController();

    final saved = await showDialog<bool>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: const Text('تعدیل موجودی'),
              content: SizedBox(
                width: 520,
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      DropdownButtonFormField<String>(
                        initialValue: warehouseId,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'انبار',
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
                        onChanged: (value) {
                          if (value != null) {
                            setDialogState(
                              () => warehouseId = value,
                            );
                          }
                        },
                      ),
                      const SizedBox(height: 12),
                      DropdownButtonFormField<String>(
                        initialValue: productId,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'کالا',
                        ),
                        items: [
                          for (final product in _products)
                            DropdownMenuItem(
                              value: product['id'].toString(),
                              child: Text(
                                product['sku'].toString() +
                                    ' — ' +
                                    product['name'].toString(),
                              ),
                            ),
                        ],
                        onChanged: (value) {
                          if (value != null) {
                            setDialogState(
                              () => productId = value,
                            );
                          }
                        },
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: quantity,
                        keyboardType:
                            const TextInputType.numberWithOptions(
                          decimal: true,
                          signed: true,
                        ),
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(
                          labelText:
                              'تغییر تعداد (+ ورود / - خروج)',
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: unitCost,
                        keyboardType:
                            const TextInputType.numberWithOptions(
                          decimal: true,
                        ),
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(
                          labelText:
                              'بهای واحد برای ورود (اختیاری)',
                          helperText:
                              'برای خروج، میانگین بهای موجودی استفاده می‌شود.',
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: reason,
                        maxLines: 2,
                        decoration: const InputDecoration(
                          labelText: 'علت تعدیل',
                        ),
                      ),
                      const SizedBox(height: 12),
                      OutlinedButton.icon(
                        onPressed: () async {
                          final picked = await showDatePicker(
                            context: context,
                            initialDate: date,
                            firstDate: DateTime(2000),
                            lastDate: DateTime(2100),
                          );

                          if (picked != null) {
                            setDialogState(
                              () => date = picked,
                            );
                          }
                        },
                        icon: const Icon(
                          Icons.calendar_month_outlined,
                        ),
                        label: Text(
                          date.year.toString() +
                              '/' +
                              date.month
                                  .toString()
                                  .padLeft(2, '0') +
                              '/' +
                              date.day
                                  .toString()
                                  .padLeft(2, '0'),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () =>
                      Navigator.pop(context, false),
                  child: const Text('انصراف'),
                ),
                FilledButton(
                  onPressed: () {
                    final delta = _number(quantity.text);
                    if (delta == null ||
                        delta == 0 ||
                        reason.text.trim().isEmpty) {
                      return;
                    }

                    Navigator.pop(context, true);
                  },
                  child: const Text('ثبت تعدیل'),
                ),
              ],
            );
          },
        );
      },
    );

    if (saved != true) {
      quantity.dispose();
      unitCost.dispose();
      reason.dispose();
      return;
    }

    try {
      await _apiClient.adjustStock(
        bearerToken: widget.accessToken,
        warehouseId: warehouseId,
        productId: productId,
        documentDate: date,
        quantityDelta: _number(quantity.text)!,
        unitCost: _number(unitCost.text),
        reason: reason.text.trim(),
      );

      await _load();
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      quantity.dispose();
      unitCost.dispose();
      reason.dispose();
    }
  }

  num? _number(String value) {
    final normalized =
        value.replaceAll(',', '').trim();
    return normalized.isEmpty
        ? null
        : num.tryParse(normalized);
  }

  void _message(String value) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(value)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final totalValue = _balances.fold<num>(
      0,
      (sum, row) =>
          sum + reportNumber(row['inventoryValue']),
    );

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('موجودی انبار'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _loading ? null : _load,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _loading ? null : _adjust,
          icon: const Icon(Icons.tune_outlined),
          label: const Text('تعدیل موجودی'),
        ),
        body: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: DropdownButtonFormField<String>(
                  initialValue: _warehouseId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'فیلتر انبار',
                    prefixIcon:
                        Icon(Icons.warehouse_outlined),
                  ),
                  items: [
                    const DropdownMenuItem<String>(
                      value: null,
                      child: Text('همه انبارها'),
                    ),
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
                  onChanged: _loading
                      ? null
                      : (value) async {
                          setState(
                            () => _warehouseId = value,
                          );
                          await _load();
                        },
                ),
              ),
            ),
            const SizedBox(height: 12),
            Card(
              child: ListTile(
                leading: const CircleAvatar(
                  child:
                      Icon(Icons.inventory_2_outlined),
                ),
                title:
                    const Text('ارزش موجودی فعلی'),
                trailing: Text(
                  formatReportMoney(totalValue) +
                      ' ریال',
                ),
              ),
            ),
            const SizedBox(height: 12),
            if (_error != null)
              Card(
                child: ListTile(
                  leading: Icon(
                    Icons.error_outline,
                    color:
                        Theme.of(context).colorScheme.error,
                  ),
                  title:
                      const Text('خطا در دریافت موجودی'),
                  subtitle: Text(_error!),
                ),
              )
            else if (_loading)
              const Padding(
                padding: EdgeInsets.all(40),
                child: Center(
                  child: CircularProgressIndicator(),
                ),
              )
            else if (_balances.isEmpty)
              const Padding(
                padding: EdgeInsets.all(40),
                child: Center(
                  child: Text(
                    'موجودی قابل نمایش وجود ندارد.',
                  ),
                ),
              )
            else
              Card(
                child: SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: DataTable(
                    columns: const [
                      DataColumn(label: Text('انبار')),
                      DataColumn(label: Text('کالا')),
                      DataColumn(
                        numeric: true,
                        label: Text('تعداد'),
                      ),
                      DataColumn(
                        numeric: true,
                        label: Text('میانگین بها'),
                      ),
                      DataColumn(
                        numeric: true,
                        label: Text('ارزش موجودی'),
                      ),
                    ],
                    rows: [
                      for (final row in _balances)
                        DataRow(
                          cells: [
                            DataCell(
                              Text(
                                row['warehouseName']
                                    .toString(),
                              ),
                            ),
                            DataCell(
                              Text(
                                row['sku'].toString() +
                                    ' — ' +
                                    row['productName']
                                        .toString(),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    row['quantity'],
                                  ),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    row['averageCost'],
                                  ),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    row['inventoryValue'],
                                  ),
                                ),
                              ),
                            ),
                          ],
                        ),
                    ],
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
