import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../accounting/report_support.dart';

class StockPage extends StatefulWidget {
  const StockPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<StockPage> createState() => _StockPageState();
}

class _StockPageState extends State<StockPage> {
  final _apiClient = ApiClient();

  List<Map<String, dynamic>> _warehouses = const [];
  List<Map<String, dynamic>> _products = const [];
  List<Map<String, dynamic>> _balances = const [];
  List<Map<String, dynamic>> _lowStock = const [];
  List<Map<String, dynamic>> _traceBalances = const [];

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
        _apiClient.getLowStockAlerts(
          bearerToken: widget.accessToken,
        ),
        _apiClient.getStockTraceBalances(
          bearerToken: widget.accessToken,
          warehouseId: _warehouseId,
        ),
      ]);

      if (!mounted) return;

      final warehouses = results[0];
      final products = results[1];
      final balances = results[2];
      final lowStock = results[3];
      final traceBalances = results[4];

      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'Warehouse',
        items: warehouses,
      );
      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'StoreProduct',
        items: products,
      );
      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'StockBalance',
        items: balances,
      );
      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'LowStockAlert',
        items: lowStock,
      );
      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'StockTrace',
        items: traceBalances,
      );

      setState(() {
        _warehouses = warehouses;
        _products = products
            .where(
              (item) =>
                  item['trackInventory'] as bool? ?? false,
            )
            .toList(growable: false);
        _balances = balances;
        _lowStock = lowStock;
        _traceBalances = traceBalances;
      });
    } on ApiException catch (error) {
      if (!mounted) return;

      if (error.statusCode != null) {
        setState(() => _error = error.message);
      } else {
        final warehouses =
            await widget.localDatabase.getCachedStoreEntities(
          companyId: widget.companyId,
          entityType: 'Warehouse',
        );
        final products =
            await widget.localDatabase.getCachedStoreEntities(
          companyId: widget.companyId,
          entityType: 'StoreProduct',
        );
        final balances =
            await widget.localDatabase.getCachedStoreEntities(
          companyId: widget.companyId,
          entityType: 'StockBalance',
        );
        final lowStock =
            await widget.localDatabase.getCachedStoreEntities(
          companyId: widget.companyId,
          entityType: 'LowStockAlert',
        );
        final trace =
            await widget.localDatabase.getCachedStoreEntities(
          companyId: widget.companyId,
          entityType: 'StockTrace',
        );

        if (!mounted) return;

        setState(() {
          _warehouses = warehouses;
          _products = products
              .where(
                (item) =>
                    item['trackInventory'] as bool? ?? false,
              )
              .toList(growable: false);
          _balances = balances;
          _lowStock = lowStock;
          _traceBalances = trace;
        });
      }
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
    final lot = TextEditingController();
    final serial = TextEditingController();
    DateTime? expiryDate;

    final saved = await showDialog<bool>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            Map<String, dynamic>? selectedProduct;

            for (final product in _products) {
              if (product['id'].toString() == productId) {
                selectedProduct = product;
                break;
              }
            }

            final tracking =
                selectedProduct?['trackingMode']?.toString() ?? 'None';

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
                      if (tracking == 'Lot') ...[
                        const SizedBox(height: 12),
                        TextField(
                          controller: lot,
                          textDirection: TextDirection.ltr,
                          decoration: const InputDecoration(
                            labelText: 'شماره لات',
                          ),
                        ),
                        const SizedBox(height: 12),
                        OutlinedButton.icon(
                          onPressed: () async {
                            final picked = await showDatePicker(
                              context: context,
                              initialDate: expiryDate ??
                                  DateTime.now().add(
                                    const Duration(days: 365),
                                  ),
                              firstDate: DateTime(2000),
                              lastDate: DateTime(2200),
                            );

                            if (picked != null) {
                              setDialogState(
                                () => expiryDate = picked,
                              );
                            }
                          },
                          icon: const Icon(Icons.event_outlined),
                          label: Text(
                            expiryDate == null
                                ? 'تاریخ انقضا (اختیاری)'
                                : formatReportDate(expiryDate),
                          ),
                        ),
                      ],
                      if (tracking == 'Serial') ...[
                        const SizedBox(height: 12),
                        TextField(
                          controller: serial,
                          textDirection: TextDirection.ltr,
                          decoration: const InputDecoration(
                            labelText: 'شماره سریال',
                          ),
                        ),
                      ],
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

                    Map<String, dynamic>? selectedProduct;
                    for (final product in _products) {
                      if (product['id'].toString() == productId) {
                        selectedProduct = product;
                        break;
                      }
                    }

                    final tracking =
                        selectedProduct?['trackingMode']?.toString() ??
                            'None';

                    if (delta == null ||
                        delta == 0 ||
                        reason.text.trim().isEmpty) {
                      return;
                    }

                    if (tracking == 'Lot' &&
                        lot.text.trim().isEmpty) {
                      return;
                    }

                    if (tracking == 'Serial' &&
                        (serial.text.trim().isEmpty ||
                            delta.abs() != 1)) {
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
      lot.dispose();
      serial.dispose();
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
        lotNumber: lot.text.trim().isEmpty
            ? null
            : lot.text.trim(),
        serialNumber: serial.text.trim().isEmpty
            ? null
            : serial.text.trim(),
        expiryDate: expiryDate,
      );

      await _load();
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      quantity.dispose();
      unitCost.dispose();
      reason.dispose();
      lot.dispose();
      serial.dispose();
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
            if (_lowStock.isNotEmpty) ...[
              Card(
                child: ExpansionTile(
                  initiallyExpanded: true,
                  leading: const CircleAvatar(
                    child: Icon(Icons.warning_amber_outlined),
                  ),
                  title: Text(
                    'هشدار کمبود موجودی (' +
                        _lowStock.length.toString() +
                        ')',
                  ),
                  subtitle: const Text(
                    'کالاهایی که موجودی آن‌ها به حداقل تعریف‌شده رسیده یا کمتر شده است.',
                  ),
                  children: [
                    for (final row in _lowStock)
                      ListTile(
                        dense: true,
                        title: Text(
                          row['sku'].toString() +
                              ' — ' +
                              row['productName'].toString(),
                        ),
                        subtitle: Text(
                          row['warehouseName'].toString() +
                              ' • موجودی: ' +
                              formatReportMoney(
                                reportNumber(row['quantity']),
                              ) +
                              ' • حداقل: ' +
                              formatReportMoney(
                                reportNumber(row['minimumStock']),
                              ),
                        ),
                        trailing: Text(
                          'کسری ' +
                              formatReportMoney(
                                reportNumber(row['shortage']),
                              ),
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(height: 12),
            ],
            if (_traceBalances.isNotEmpty) ...[
              Card(
                child: ExpansionTile(
                  title: const Text('رهگیری لات / سریال / انقضا'),
                  subtitle: Text(
                    _traceBalances.length.toString() +
                        ' موجودی رهگیری‌شده',
                  ),
                  children: [
                    for (final row in _traceBalances)
                      ListTile(
                        dense: true,
                        title: Text(
                          row['sku'].toString() +
                              ' — ' +
                              row['productName'].toString(),
                        ),
                        subtitle: Text(
                          row['warehouseName'].toString() +
                              (row['lotNumber'] == null
                                  ? ''
                                  : ' • لات ' +
                                      row['lotNumber'].toString()) +
                              (row['serialNumber'] == null
                                  ? ''
                                  : ' • سریال ' +
                                      row['serialNumber'].toString()) +
                              (row['expiryDate'] == null
                                  ? ''
                                  : ' • انقضا ' +
                                      formatReportDate(
                                        DateTime.parse(
                                          row['expiryDate'].toString(),
                                        ),
                                      )),
                        ),
                        trailing: Text(
                          formatReportMoney(
                            reportNumber(row['quantity']),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(height: 12),
            ],
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
                      DataColumn(label: Text('وضعیت')),
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
                            DataCell(
                              Text(
                                (row['isLowStock'] as bool? ?? false)
                                    ? 'کمبود / نقطه سفارش'
                                    : 'عادی',
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
