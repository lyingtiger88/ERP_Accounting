import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../accounting/report_support.dart';

class NewPurchaseReceiptPage extends StatefulWidget {
  const NewPurchaseReceiptPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
    this.purchaseOrder,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;
  final Map<String, dynamic>? purchaseOrder;

  @override
  State<NewPurchaseReceiptPage> createState() =>
      _NewPurchaseReceiptPageState();
}

class _NewPurchaseReceiptPageState
    extends State<NewPurchaseReceiptPage> {
  final _apiClient = ApiClient();
  final _description = TextEditingController();
  final _exchangeRate = TextEditingController(text: '1');
  final List<_PurchaseRowEditor> _rows = [_PurchaseRowEditor()];

  List<Map<String, dynamic>> _products = const [];
  List<Map<String, dynamic>> _warehouses = const [];
  List<Map<String, dynamic>> _currencies = const [];
  List<CachedFiscalYear> _fiscalYears = const [];
  List<CachedDetailAccount> _suppliers = const [];

  String? _warehouseId;
  String? _fiscalYearId;
  String? _currencyId;
  String? _supplierId;
  String _paymentType = 'Credit';
  DateTime _documentDate = DateTime.now();
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
    _exchangeRate.dispose();
    for (final row in _rows) {
      row.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    try {
      List<Map<String, dynamic>> products;
      List<Map<String, dynamic>> warehouses;

      try {
        final results = await Future.wait([
          _apiClient.getStoreProducts(
            bearerToken: widget.accessToken,
          ),
          _apiClient.getWarehouses(
            bearerToken: widget.accessToken,
          ),
        ]);

        products = results[0];
        warehouses = results[1];

        await widget.localDatabase.replaceStoreEntities(
          companyId: widget.companyId,
          entityType: 'StoreProduct',
          items: products,
        );
        await widget.localDatabase.replaceStoreEntities(
          companyId: widget.companyId,
          entityType: 'Warehouse',
          items: warehouses,
        );
      } on ApiException catch (error) {
        if (error.statusCode != null) rethrow;

        products = await widget.localDatabase.getCachedStoreEntities(
          companyId: widget.companyId,
          entityType: 'StoreProduct',
        );
        warehouses = await widget.localDatabase.getCachedStoreEntities(
          companyId: widget.companyId,
          entityType: 'Warehouse',
        );
      }

      List<Map<String, dynamic>> currencies = const [];
      try {
        currencies = await _apiClient.getCurrencies(
          bearerToken: widget.accessToken,
        );
      } catch (_) {
        // Base-currency purchase remains available offline.
      }

            final years =
          await widget.localDatabase.getCachedFiscalYears(
        widget.companyId,
      );
      final details =
          await widget.localDatabase.getCachedDetailAccounts(
        widget.companyId,
      );

      if (!mounted) return;

      products = products
          .where(
            (x) =>
                (x['isActive'] as bool? ?? true) &&
                (x['trackInventory'] as bool? ?? false),
          )
          .toList(growable: false);
      warehouses = warehouses
          .where((x) => x['isActive'] as bool? ?? true)
          .toList(growable: false);
      final suppliers = details
          .where(
            (x) =>
                x.isActive &&
                (x.type == 'Supplier' ||
                    x.type == 'Person'),
          )
          .toList(growable: false);

      CachedFiscalYear? selectedYear;
      for (final year in years) {
        if (!year.isClosed && year.contains(_documentDate)) {
          selectedYear = year;
          if (year.isDefault) break;
        }
      }

      final order = widget.purchaseOrder;
      final prefilledRows = <_PurchaseRowEditor>[];

      if (order != null) {
        for (final raw
            in (order['lines'] as List<dynamic>? ?? const [])) {
          final line = Map<String, dynamic>.from(raw as Map);
          final ordered = reportNumber(line['quantity']);
          final received = reportNumber(line['receivedQuantity']);
          final remaining = ordered - received;
          if (remaining <= 0) continue;

          final productId = line['productId']?.toString();
          Map<String, dynamic>? product;
          for (final candidate in products) {
            if (candidate['id'].toString() == productId) {
              product = candidate;
              break;
            }
          }

          final tracking =
              product?['trackingMode']?.toString() ?? 'None';
          final splitCount =
              tracking == 'Serial' && remaining == remaining.round()
                  ? remaining.toInt()
                  : 1;

          for (var index = 0; index < splitCount; index++) {
            final row = _PurchaseRowEditor();
            row.purchaseOrderLineId = line['id']?.toString();
            row.productId = productId;
            row.quantity.text =
                tracking == 'Serial' ? '1' : remaining.toString();
            row.unitCost.text =
                reportNumber(line['unitCost']).toString();

            final ratio = ordered == 0
                ? 0
                : (tracking == 'Serial' ? 1 : remaining) / ordered;
            row.discount.text =
                (reportNumber(line['discountAmount']) * ratio)
                    .toString();
            row.tax.text =
                (reportNumber(line['taxAmount']) * ratio)
                    .toString();
            prefilledRows.add(row);
          }
        }
      }

      setState(() {
        _products = products;
        _warehouses = warehouses;
        _currencies = currencies;
        _fiscalYears = years;
        _suppliers = suppliers;
        _warehouseId = order?['warehouseId']?.toString() ??
            (warehouses.isEmpty
                ? null
                : warehouses.first['id'].toString());
        _fiscalYearId =
            order?['fiscalYearId']?.toString() ?? selectedYear?.id;
        _supplierId =
            order?['supplierDetailAccountId']?.toString() ??
                (suppliers.isEmpty ? null : suppliers.first.id);
        _currencyId = order?['currencyId']?.toString();
        if (order?['exchangeRate'] != null) {
          _exchangeRate.text =
              reportNumber(order!['exchangeRate']).toString();
        }
        if (order != null) {
          for (final row in _rows) {
            row.dispose();
          }
          _rows
            ..clear()
            ..addAll(prefilledRows);
          if (_rows.isEmpty) {
            _rows.add(_PurchaseRowEditor());
          }
        }
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

  Map<String, dynamic>? _currency(String? id) {
    if (id == null) return null;
    for (final item in _currencies) {
      if (item['id'].toString() == id) return item;
    }
    return null;
  }

  String get _currencyCode {
    if (_currencyId != null) {
      return _currency(_currencyId)?['code']?.toString() ?? 'FX';
    }

    for (final item in _currencies) {
      if (item['isBase'] as bool? ?? false) {
        return item['code'].toString();
      }
    }

    return 'BASE';
  }

  Future<void> _selectCurrency(String? value) async {
    setState(() {
      _currencyId = value;
      if (value == null) {
        _exchangeRate.text = '1';
      }
    });

    if (value != null) {
      try {
        final rate = await _apiClient.getCurrencyAccountingRate(
          bearerToken: widget.accessToken,
          currencyId: value,
          date: _documentDate,
        );

        if (!mounted) return;
        setState(() => _exchangeRate.text = rate.toString());
      } on ApiException catch (error) {
        if (!mounted) return;
        setState(() => _exchangeRate.clear());
        _message(error.message);
      }
    }

    _applyDefaultCosts();
  }

  void _applyDefaultCosts() {
    final rate = _number(_exchangeRate.text);

    setState(() {
      for (final row in _rows) {
        final product = _product(row.productId);
        if (product == null) continue;

        final baseCost =
            reportNumber(product['defaultPurchasePrice']);
        row.unitCost.text = _currencyId != null && rate > 0
            ? (baseCost / rate).toStringAsFixed(4)
            : baseCost.toString();
      }
    });
  }

  Map<String, dynamic>? _product(String? id) {
    if (id == null) return null;
    for (final item in _products) {
      if (item['id'].toString() == id) return item;
    }
    return null;
  }

  num _number(String value) {
    return num.tryParse(
          value.replaceAll(',', '').trim(),
        ) ??
        0;
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _documentDate,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );

    if (picked == null || !mounted) return;

    setState(() {
      _documentDate = picked;
      for (final year in _fiscalYears) {
        if (!year.isClosed && year.contains(picked)) {
          _fiscalYearId = year.id;
          break;
        }
      }
    });

    if (_currencyId != null) {
      await _selectCurrency(_currencyId);
    }
  }

  void _selectProduct(
    _PurchaseRowEditor row,
    String? productId,
  ) {
    setState(() {
      row.productId = productId;
      final product = _product(productId);
      if (product != null) {
        final baseCost =
            reportNumber(product['defaultPurchasePrice']);
        final rate = _number(_exchangeRate.text);
        row.unitCost.text =
            _currencyId != null && rate > 0
                ? (baseCost / rate).toStringAsFixed(4)
                : baseCost.toString();
      }
    });
  }

  Future<void> _pickExpiry(
    _PurchaseRowEditor row,
  ) async {
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

  Future<void> _scanProduct() async {
    final controller = TextEditingController();

    final code = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('اسکن بارکد / SKU'),
        content: TextField(
          controller: controller,
          autofocus: true,
          textDirection: TextDirection.ltr,
          decoration: const InputDecoration(
            labelText: 'بارکد را اسکن کنید',
          ),
          onSubmitted: (value) =>
              Navigator.pop(context, value.trim()),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(
              context,
              controller.text.trim(),
            ),
            child: const Text('ثبت'),
          ),
        ],
      ),
    );

    controller.dispose();

    if (code == null || code.isEmpty) return;

    Map<String, dynamic>? product;

    try {
      product = await _apiClient.findStoreProduct(
        bearerToken: widget.accessToken,
        code: code,
      );
    } on ApiException catch (error) {
      if (error.statusCode != null) {
        _message(error.message);
        return;
      }
    }

    product ??= _products.cast<Map<String, dynamic>?>().firstWhere(
          (item) =>
              item?['sku']?.toString() == code ||
              item?['barcode']?.toString() == code,
          orElse: () => null,
        );

    if (product == null) {
      _message('کالایی با این بارکد یا SKU پیدا نشد.');
      return;
    }

    _PurchaseRowEditor? target;

    for (final row in _rows) {
      if (row.productId == null) {
        target = row;
        break;
      }
    }

    target ??= _PurchaseRowEditor();

    if (!_rows.contains(target)) {
      _rows.add(target);
    }

    setState(() {
      target!.productId = product!['id'].toString();
      final baseCost =
          reportNumber(product['defaultPurchasePrice']);
      final rate = _number(_exchangeRate.text);
      target.unitCost.text =
          _currencyId != null && rate > 0
              ? (baseCost / rate).toStringAsFixed(4)
              : baseCost.toString();
      if (product['trackingMode']?.toString() == 'Serial') {
        target.quantity.text = '1';
      }
    });
  }

  Future<void> _save() async {
    if (_fiscalYearId == null || _warehouseId == null) {
      _message('سال مالی و انبار الزامی هستند.');
      return;
    }

    if (_paymentType == 'Credit' && _supplierId == null) {
      _message('برای خرید نسیه، تامین‌کننده الزامی است.');
      return;
    }

    final exchangeRate = _number(_exchangeRate.text);
    if (_currencyId != null && exchangeRate <= 0) {
      _message('برای خرید ارزی، نرخ حسابداری معتبر الزامی است.');
      return;
    }

    final lines = <Map<String, dynamic>>[];

    for (final row in _rows) {
      if (row.productId == null) continue;

      final quantity = _number(row.quantity.text);
      if (quantity <= 0) {
        _message('تعداد باید بزرگ‌تر از صفر باشد.');
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
        if (row.serial.text.trim().isEmpty) {
          _message('برای کالای سریالی، شماره سریال الزامی است.');
          return;
        }
        if (quantity != 1) {
          _message('هر ردیف کالای سریالی باید تعداد ۱ داشته باشد.');
          return;
        }
      }

      lines.add({
        'productId': row.productId,
        'purchaseOrderLineId': row.purchaseOrderLineId,
        'quantity': quantity,
        'unitCost': row.unitCost.text.trim().isEmpty
            ? null
            : _number(row.unitCost.text),
        'discountAmount': _number(row.discount.text),
        'taxAmount': _number(row.tax.text),
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
      _message('حداقل یک قلم خرید لازم است.');
      return;
    }

    setState(() => _saving = true);

    try {
      final result = await _apiClient.createPurchaseReceipt(
        bearerToken: widget.accessToken,
        fiscalYearId: _fiscalYearId!,
        documentDate: _documentDate,
        warehouseId: _warehouseId!,
        supplierDetailAccountId: _supplierId,
        paymentType: _paymentType,
        description: _description.text.trim().isEmpty
            ? null
            : _description.text.trim(),
        lines: lines,
        currencyId: _currencyId,
        exchangeRate:
            _currencyId == null ? null : exchangeRate,
        purchaseOrderId: widget.purchaseOrder?['id']?.toString(),
      );

      if (!mounted) return;
      Navigator.pop(context, result);
    } on ApiException catch (error) {
      if (error.statusCode == null) {
        try {
          final localId =
              await widget.localDatabase.saveLocalStoreDraft(
            companyId: widget.companyId,
            entityType: 'StorePurchaseReceiptDraft',
            payload: {
              'fiscalYearId': _fiscalYearId,
              'purchaseOrderId':
                  widget.purchaseOrder?['id']?.toString(),
              'documentDate': _dateOnly(_documentDate),
              'warehouseId': _warehouseId,
              'supplierDetailAccountId': _supplierId,
              'paymentType': _paymentType,
              'description': _description.text.trim().isEmpty
                  ? null
                  : _description.text.trim(),
              'currencyId': _currencyId,
              'currencyCode': _currencyCode,
              'exchangeRate':
                  _currencyId == null ? null : exchangeRate,
              'lines': lines,
            },
          );

          if (!mounted) return;

          Navigator.pop(
            context,
            {
              'id': localId,
              'number': 'LOCAL-' +
                  localId.substring(0, 8).toUpperCase(),
              'status': 'LocalPending',
            },
          );
        } on StateError catch (localError) {
          _message(localError.message);
        }
      } else {
        _message(error.message);
      }
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

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(text)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final subtotal = _rows.fold<num>(0, (sum, row) {
      return sum +
          _number(row.quantity.text) *
              _number(row.unitCost.text);
    });
    final discount = _rows.fold<num>(
      0,
      (sum, row) => sum + _number(row.discount.text),
    );
    final tax = _rows.fold<num>(
      0,
      (sum, row) => sum + _number(row.tax.text),
    );

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: Text(
            widget.purchaseOrder == null
                ? 'رسید خرید جدید'
                : 'دریافت سفارش ' +
                    widget.purchaseOrder!['number'].toString(),
          ),
          actions: [
            IconButton(
              tooltip: 'اسکن بارکد',
              onPressed: _saving ? null : _scanProduct,
              icon: const Icon(Icons.qr_code_scanner_outlined),
            ),
          ],
        ),
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
                                width: 240,
                                child: DropdownButtonFormField<String>(
                                  initialValue: _fiscalYearId,
                                  isExpanded: true,
                                  decoration: const InputDecoration(
                                    labelText: 'سال مالی',
                                  ),
                                  items: [
                                    for (final year in _fiscalYears)
                                      if (!year.isClosed)
                                        DropdownMenuItem(
                                          value: year.id,
                                          child: Text(year.name),
                                        ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) => setState(
                                            () => _fiscalYearId = value,
                                          ),
                                ),
                              ),
                              SizedBox(
                                width: 240,
                                child: DropdownButtonFormField<String>(
                                  initialValue: _warehouseId,
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
                                            () => _warehouseId = value,
                                          ),
                                ),
                              ),
                              OutlinedButton.icon(
                                onPressed: _saving ? null : _pickDate,
                                icon: const Icon(
                                  Icons.calendar_month_outlined,
                                ),
                                label: Text(
                                  formatReportDate(_documentDate),
                                ),
                              ),
                              SizedBox(
                                width: 220,
                                child: DropdownButtonFormField<String>(
                                  initialValue: _currencyId,
                                  isExpanded: true,
                                  decoration: const InputDecoration(
                                    labelText: 'ارز خرید',
                                    prefixIcon: Icon(
                                      Icons.currency_exchange_outlined,
                                    ),
                                  ),
                                  items: [
                                    const DropdownMenuItem<String>(
                                      value: null,
                                      child: Text('ارز پایه شرکت'),
                                    ),
                                    for (final item in _currencies)
                                      if ((item['isActive'] as bool? ?? true) &&
                                          !(item['isBase'] as bool? ?? false))
                                        DropdownMenuItem(
                                          value: item['id'].toString(),
                                          child: Text(
                                            item['code'].toString() +
                                                ' — ' +
                                                item['name'].toString(),
                                          ),
                                        ),
                                  ],
                                  onChanged:
                                      _saving ? null : _selectCurrency,
                                ),
                              ),
                              SizedBox(
                                width: 190,
                                child: TextField(
                                  controller: _exchangeRate,
                                  enabled:
                                      !_saving && _currencyId != null,
                                  textDirection: TextDirection.ltr,
                                  keyboardType:
                                      const TextInputType.numberWithOptions(
                                    decimal: true,
                                  ),
                                  decoration: InputDecoration(
                                    labelText: 'نرخ حسابداری',
                                    helperText: _currencyId == null
                                        ? 'ارز پایه'
                                        : 'ارز پایه / 1 ' +
                                            _currencyCode,
                                  ),
                                ),
                              ),

                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(18),
                          child: Wrap(
                            spacing: 12,
                            runSpacing: 12,
                            children: [
                              SizedBox(
                                width: 200,
                                child: DropdownButtonFormField<String>(
                                  initialValue: _paymentType,
                                  decoration: const InputDecoration(
                                    labelText: 'نوع خرید',
                                  ),
                                  items: const [
                                    DropdownMenuItem(
                                      value: 'Credit',
                                      child: Text('نسیه'),
                                    ),
                                    DropdownMenuItem(
                                      value: 'Cash',
                                      child: Text('نقدی'),
                                    ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) {
                                          if (value == null) return;
                                          setState(() {
                                            _paymentType = value;
                                            if (value == 'Cash') {
                                              _supplierId = null;
                                            }
                                          });
                                        },
                                ),
                              ),
                              SizedBox(
                                width: 320,
                                child: DropdownButtonFormField<String>(
                                  initialValue: _supplierId,
                                  isExpanded: true,
                                  decoration: const InputDecoration(
                                    labelText: 'تامین‌کننده',
                                  ),
                                  items: [
                                    const DropdownMenuItem<String>(
                                      value: null,
                                      child: Text('بدون تامین‌کننده'),
                                    ),
                                    for (final supplier in _suppliers)
                                      DropdownMenuItem(
                                        value: supplier.id,
                                        child: Text(
                                          supplier.code +
                                              ' — ' +
                                              supplier.name,
                                        ),
                                      ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) => setState(
                                            () => _supplierId = value,
                                          ),
                                ),
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
                                      'اقلام خرید',
                                      style: TextStyle(
                                        fontSize: 18,
                                        fontWeight: FontWeight.w700,
                                      ),
                                    ),
                                  ),
                                  TextButton.icon(
                                    onPressed: _saving ||
                                            widget.purchaseOrder != null
                                        ? null
                                        : () => setState(
                                              () => _rows.add(
                                                _PurchaseRowEditor(),
                                              ),
                                            ),
                                    icon: const Icon(Icons.add),
                                    label: const Text('افزودن ردیف'),
                                  ),
                                ],
                              ),
                              for (var index = 0;
                                  index < _rows.length;
                                  index++) ...[
                                _PurchaseLineCard(
                                  row: _rows[index],
                                  products: _products,
                                  saving: _saving,
                                  onProductChanged:
                                      _rows[index].purchaseOrderLineId != null
                                          ? null
                                          : (value) => _selectProduct(
                                                _rows[index],
                                                value,
                                              ),
                                  onChanged: () => setState(() {}),
                                  onPickExpiry: () =>
                                      _pickExpiry(_rows[index]),
                                  onRemove:
                                      _rows[index].purchaseOrderLineId != null
                                          ? null
                                          : () {
                                    if (_rows.length <= 1) return;
                                    final removed =
                                        _rows.removeAt(index);
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
                          labelText: 'شرح خرید',
                        ),
                      ),
                      const SizedBox(height: 12),
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(18),
                          child: Wrap(
                            spacing: 24,
                            runSpacing: 8,
                            children: [
                              Text(
                                'ناخالص: ' +
                                    formatReportMoney(subtotal),
                              ),
                              Text(
                                'تخفیف: ' +
                                    formatReportMoney(discount),
                              ),
                              Text(
                                'مالیات: ' +
                                    formatReportMoney(tax),
                              ),
                              Text(
                                'قابل پرداخت: ' +
                                    formatReportMoney(
                                      subtotal - discount + tax,
                                    ) +
                                    ' ریال',
                                style: const TextStyle(
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 18),
                      FilledButton.icon(
                        onPressed: _saving ? null : _save,
                        icon: const Icon(Icons.save_outlined),
                        label: const Text(
                          'ذخیره رسید خرید به‌صورت پیش‌نویس',
                        ),
                      ),
                    ],
                  ),
      ),
    );
  }
}

class _PurchaseLineCard extends StatelessWidget {
  const _PurchaseLineCard({
    required this.row,
    required this.products,
    required this.saving,
    required this.onProductChanged,
    required this.onChanged,
    required this.onPickExpiry,
    required this.onRemove,
  });

  final _PurchaseRowEditor row;
  final List<Map<String, dynamic>> products;
  final bool saving;
  final ValueChanged<String?>? onProductChanged;
  final VoidCallback onChanged;
  final VoidCallback onPickExpiry;
  final VoidCallback? onRemove;

  Map<String, dynamic>? _product() {
    for (final item in products) {
      if (item['id'].toString() == row.productId) {
        return item;
      }
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final product = _product();
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
                  for (final product in products)
                    DropdownMenuItem(
                      value: product['id'].toString(),
                      child: Text(
                        product['sku'].toString() +
                            ' — ' +
                            product['name'].toString(),
                      ),
                    ),
                ],
                onChanged:
                    saving ? null : onProductChanged,
              ),
            ),
            _PurchaseField(
              controller: row.quantity,
              label: 'تعداد',
              width: 110,
              onChanged: onChanged,
            ),
            _PurchaseField(
              controller: row.unitCost,
              label: 'بهای واحد',
              width: 150,
              onChanged: onChanged,
            ),
            _PurchaseField(
              controller: row.discount,
              label: 'تخفیف',
              width: 130,
              onChanged: onChanged,
            ),
            _PurchaseField(
              controller: row.tax,
              label: 'مالیات',
              width: 130,
              onChanged: onChanged,
            ),
            if (tracking == 'Lot')
              SizedBox(
                width: 150,
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
                width: 170,
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
              tooltip: 'حذف ردیف',
              onPressed: saving ? null : onRemove,
              icon: const Icon(Icons.delete_outline),
            ),
          ],
        ),
      ),
    );
  }
}

class _PurchaseField extends StatelessWidget {
  const _PurchaseField({
    required this.controller,
    required this.label,
    required this.width,
    required this.onChanged,
  });

  final TextEditingController controller;
  final String label;
  final double width;
  final VoidCallback onChanged;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: width,
      child: TextField(
        controller: controller,
        keyboardType:
            const TextInputType.numberWithOptions(decimal: true),
        textDirection: TextDirection.ltr,
        onChanged: (_) => onChanged(),
        decoration: InputDecoration(labelText: label),
      ),
    );
  }
}

class _PurchaseRowEditor {
  String? productId;
  String? purchaseOrderLineId;
  final quantity = TextEditingController(text: '1');
  final unitCost = TextEditingController();
  final discount = TextEditingController(text: '0');
  final tax = TextEditingController(text: '0');
  final lot = TextEditingController();
  final serial = TextEditingController();
  DateTime? expiryDate;

  void dispose() {
    quantity.dispose();
    unitCost.dispose();
    discount.dispose();
    tax.dispose();
    lot.dispose();
    serial.dispose();
  }
}
