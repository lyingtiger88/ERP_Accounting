import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../accounting/report_support.dart';

class NewSalesInvoicePage extends StatefulWidget {
  const NewSalesInvoicePage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<NewSalesInvoicePage> createState() =>
      _NewSalesInvoicePageState();
}

class _NewSalesInvoicePageState
    extends State<NewSalesInvoicePage> {
  final _apiClient = ApiClient();
  final _description = TextEditingController();
  final List<_InvoiceRowEditor> _rows = [];

  List<Map<String, dynamic>> _products = const [];
  List<Map<String, dynamic>> _warehouses = const [];
  List<CachedFiscalYear> _fiscalYears = const [];
  List<CachedDetailAccount> _customers = const [];

  String? _warehouseId;
  String? _fiscalYearId;
  String? _customerId;
  String _paymentType = 'Cash';
  DateTime _documentDate = DateTime.now();
  bool _loading = true;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _rows.add(_InvoiceRowEditor());
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

      final fiscalYears =
          await widget.localDatabase.getCachedFiscalYears(
        widget.companyId,
      );
      final detailAccounts =
          await widget.localDatabase.getCachedDetailAccounts(
        widget.companyId,
      );

      if (!mounted) return;

      products = products
          .where((x) => x['isActive'] as bool? ?? true)
          .toList(growable: false);
      warehouses = warehouses
          .where((x) => x['isActive'] as bool? ?? true)
          .toList(growable: false);
      final customers = detailAccounts
          .where(
            (x) =>
                x.isActive &&
                (x.type == 'Customer' ||
                    x.type == 'Person'),
          )
          .toList(growable: false);

      CachedFiscalYear? selectedYear;
      for (final year in fiscalYears) {
        if (year.isDefault &&
            !year.isClosed &&
            year.contains(_documentDate)) {
          selectedYear = year;
          break;
        }
      }

      if (selectedYear == null) {
        for (final year in fiscalYears) {
          if (!year.isClosed &&
              year.contains(_documentDate)) {
            selectedYear = year;
            break;
          }
        }
      }

      if (selectedYear == null) {
        for (final year in fiscalYears) {
          if (!year.isClosed) {
            selectedYear = year;
            break;
          }
        }
      }

      setState(() {
        _products = products;
        _warehouses = warehouses;
        _fiscalYears = fiscalYears;
        _customers = customers;
        _warehouseId = warehouses.isEmpty
            ? null
            : warehouses.first['id'].toString();
        _fiscalYearId = selectedYear?.id;
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
    for (final item in _products) {
      if (item['id'].toString() == id) return item;
    }
    return null;
  }

  num _number(String text) {
    return num.tryParse(
          text.replaceAll(',', '').trim(),
        ) ??
        0;
  }

  num get _subtotal {
    return _rows.fold<num>(0, (sum, row) {
      final product = _product(row.productId);
      final quantity = _number(row.quantity.text);
      final price = row.unitPrice.text.trim().isEmpty
          ? reportNumber(product?['salesPrice'])
          : _number(row.unitPrice.text);
      return sum + quantity * price;
    });
  }

  num get _discount {
    return _rows.fold<num>(
      0,
      (sum, row) => sum + _number(row.discount.text),
    );
  }

  num get _tax {
    return _rows.fold<num>(
      0,
      (sum, row) => sum + _number(row.tax.text),
    );
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
  }

  void _selectProduct(
    _InvoiceRowEditor row,
    String? productId,
  ) {
    setState(() {
      row.productId = productId;
      final product = _product(productId);
      if (product != null) {
        row.unitPrice.text =
            reportNumber(product['salesPrice']).toString();
      }
    });
  }

  void _addRow() {
    setState(() => _rows.add(_InvoiceRowEditor()));
  }

  void _removeRow(int index) {
    if (_rows.length <= 1) return;

    final row = _rows.removeAt(index);
    row.dispose();
    setState(() {});
  }

  Future<void> _pickExpiry(
    _InvoiceRowEditor row,
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

  Future<void> _save() async {
    final fiscalYearId = _fiscalYearId;
    final warehouseId = _warehouseId;

    if (fiscalYearId == null) {
      _message('سال مالی باز برای تاریخ فاکتور انتخاب نشده است.');
      return;
    }

    if (warehouseId == null) {
      _message('انبار فعال تعریف نشده است.');
      return;
    }

    if (_paymentType == 'Credit' && _customerId == null) {
      _message('برای فروش نسیه انتخاب مشتری الزامی است.');
      return;
    }

    final lines = <Map<String, dynamic>>[];

    for (final row in _rows) {
      if (row.productId == null) continue;

      final quantity = _number(row.quantity.text);

      if (quantity <= 0) {
        _message('تعداد هر ردیف باید بزرگ‌تر از صفر باشد.');
        return;
      }

      final product = _product(row.productId);
      final defaultPrice =
          reportNumber(product?['salesPrice']);
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
        'quantity': quantity,
        'unitPrice': row.unitPrice.text.trim().isEmpty
            ? defaultPrice
            : _number(row.unitPrice.text),
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
      _message('حداقل یک کالا یا خدمت به فاکتور اضافه کنید.');
      return;
    }

    setState(() => _saving = true);

    try {
      final invoice = await _apiClient.createSalesInvoice(
        bearerToken: widget.accessToken,
        fiscalYearId: fiscalYearId,
        documentDate: _documentDate,
        warehouseId: warehouseId,
        customerDetailAccountId: _customerId,
        paymentType: _paymentType,
        description: _description.text.trim().isEmpty
            ? null
            : _description.text.trim(),
        lines: lines,
      );

      if (!mounted) return;

      Navigator.pop(
        context,
        invoice,
      );
    } on ApiException catch (error) {
      if (error.statusCode == null) {
        final localId =
            await widget.localDatabase.saveLocalStoreDraft(
          companyId: widget.companyId,
          entityType: 'StoreSalesInvoiceDraft',
          payload: {
            'fiscalYearId': fiscalYearId,
            'documentDate': _dateOnly(_documentDate),
            'warehouseId': warehouseId,
            'customerDetailAccountId': _customerId,
            'paymentType': _paymentType,
            'description': _description.text.trim().isEmpty
                ? null
                : _description.text.trim(),
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

  void _message(String value) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(value)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final grandTotal = _subtotal - _discount + _tax;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('فاکتور فروش جدید'),
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
                                child:
                                    DropdownButtonFormField<String>(
                                  initialValue: _fiscalYearId,
                                  isExpanded: true,
                                  decoration:
                                      const InputDecoration(
                                    labelText: 'سال مالی',
                                  ),
                                  items: [
                                    for (final year
                                        in _fiscalYears)
                                      if (!year.isClosed)
                                        DropdownMenuItem(
                                          value: year.id,
                                          child: Text(year.name),
                                        ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) {
                                          setState(
                                            () => _fiscalYearId =
                                                value,
                                          );
                                        },
                                ),
                              ),
                              SizedBox(
                                width: 240,
                                child:
                                    DropdownButtonFormField<String>(
                                  initialValue: _warehouseId,
                                  isExpanded: true,
                                  decoration:
                                      const InputDecoration(
                                    labelText: 'انبار',
                                  ),
                                  items: [
                                    for (final warehouse
                                        in _warehouses)
                                      DropdownMenuItem(
                                        value: warehouse['id']
                                            .toString(),
                                        child: Text(
                                          warehouse['code']
                                                  .toString() +
                                              ' — ' +
                                              warehouse['name']
                                                  .toString(),
                                        ),
                                      ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) {
                                          setState(
                                            () => _warehouseId =
                                                value,
                                          );
                                        },
                                ),
                              ),
                              OutlinedButton.icon(
                                onPressed:
                                    _saving ? null : _pickDate,
                                icon: const Icon(
                                  Icons.calendar_month_outlined,
                                ),
                                label: Text(
                                  formatReportDate(
                                    _documentDate,
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
                                width: 210,
                                child:
                                    DropdownButtonFormField<String>(
                                  initialValue: _paymentType,
                                  decoration:
                                      const InputDecoration(
                                    labelText: 'نوع فروش',
                                  ),
                                  items: const [
                                    DropdownMenuItem(
                                      value: 'Cash',
                                      child: Text('نقدی'),
                                    ),
                                    DropdownMenuItem(
                                      value: 'Credit',
                                      child: Text('نسیه'),
                                    ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) {
                                          if (value == null) return;
                                          setState(() {
                                            _paymentType = value;
                                            if (value == 'Cash') {
                                              _customerId = null;
                                            }
                                          });
                                        },
                                ),
                              ),
                              SizedBox(
                                width: 320,
                                child:
                                    DropdownButtonFormField<String>(
                                  initialValue: _customerId,
                                  isExpanded: true,
                                  decoration:
                                      const InputDecoration(
                                    labelText:
                                        'مشتری / تفصیلی',
                                  ),
                                  items: [
                                    const DropdownMenuItem<String>(
                                      value: null,
                                      child: Text(
                                        'بدون مشتری مشخص',
                                      ),
                                    ),
                                    for (final customer
                                        in _customers)
                                      DropdownMenuItem(
                                        value: customer.id,
                                        child: Text(
                                          customer.code +
                                              ' — ' +
                                              customer.name,
                                        ),
                                      ),
                                  ],
                                  onChanged:
                                      _paymentType == 'Credit' ||
                                              !_saving
                                          ? (value) {
                                              setState(
                                                () => _customerId =
                                                    value,
                                              );
                                            }
                                          : null,
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
                            crossAxisAlignment:
                                CrossAxisAlignment.stretch,
                            children: [
                              Row(
                                children: [
                                  const Expanded(
                                    child: Text(
                                      'اقلام فاکتور',
                                      style: TextStyle(
                                        fontSize: 18,
                                        fontWeight:
                                            FontWeight.w700,
                                      ),
                                    ),
                                  ),
                                  TextButton.icon(
                                    onPressed:
                                        _saving ? null : _addRow,
                                    icon: const Icon(Icons.add),
                                    label:
                                        const Text('افزودن ردیف'),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 12),
                              for (var index = 0;
                                  index < _rows.length;
                                  index++) ...[
                                _InvoiceLineCard(
                                  row: _rows[index],
                                  products: _products,
                                  saving: _saving,
                                  onProductChanged: (value) =>
                                      _selectProduct(
                                    _rows[index],
                                    value,
                                  ),
                                  onChanged: () =>
                                      setState(() {}),
                                  onPickExpiry: () =>
                                      _pickExpiry(_rows[index]),
                                  onRemove: () =>
                                      _removeRow(index),
                                ),
                                if (index != _rows.length - 1)
                                  const SizedBox(height: 10),
                              ],
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: _description,
                        maxLines: 2,
                        decoration: const InputDecoration(
                          labelText: 'شرح فاکتور',
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
                                'جمع ناخالص: ' +
                                    formatReportMoney(
                                      _subtotal,
                                    ) +
                                    ' ریال',
                              ),
                              Text(
                                'تخفیف: ' +
                                    formatReportMoney(
                                      _discount,
                                    ) +
                                    ' ریال',
                              ),
                              Text(
                                'مالیات: ' +
                                    formatReportMoney(
                                      _tax,
                                    ) +
                                    ' ریال',
                              ),
                              Text(
                                'قابل پرداخت: ' +
                                    formatReportMoney(
                                      grandTotal,
                                    ) +
                                    ' ریال',
                                style: const TextStyle(
                                  fontWeight:
                                      FontWeight.w800,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 18),
                      FilledButton.icon(
                        onPressed: _saving ? null : _save,
                        icon: _saving
                            ? const SizedBox(
                                width: 18,
                                height: 18,
                                child:
                                    CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(
                                Icons.save_outlined,
                              ),
                        label: const Text(
                          'ذخیره فاکتور به‌صورت پیش‌نویس',
                        ),
                      ),
                      const SizedBox(height: 10),
                      const Text(
                        'ثبت قطعی از لیست فاکتورها انجام می‌شود. در آن مرحله موجودی کالا کاهش پیدا می‌کند و در صورت فعال بودن نگاشت حساب‌ها، سند حسابداری فروش و بهای تمام‌شده به‌صورت خودکار ساخته می‌شود.',
                        style: TextStyle(fontSize: 12),
                      ),
                    ],
                  ),
      ),
    );
  }
}

class _InvoiceLineCard extends StatelessWidget {
  const _InvoiceLineCard({
    required this.row,
    required this.products,
    required this.saving,
    required this.onProductChanged,
    required this.onChanged,
    required this.onPickExpiry,
    required this.onRemove,
  });

  final _InvoiceRowEditor row;
  final List<Map<String, dynamic>> products;
  final bool saving;
  final ValueChanged<String?> onProductChanged;
  final VoidCallback onChanged;
  final VoidCallback onPickExpiry;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    Map<String, dynamic>? selectedProduct;

    for (final product in products) {
      if (product['id'].toString() == row.productId) {
        selectedProduct = product;
        break;
      }
    }

    final tracking =
        selectedProduct?['trackingMode']?.toString() ?? 'None';

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Wrap(
          spacing: 10,
          runSpacing: 10,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 310,
              child: DropdownButtonFormField<String>(
                initialValue: row.productId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'کالا / خدمت',
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
            _AmountField(
              controller: row.quantity,
              label: 'تعداد',
              width: 120,
              onChanged: onChanged,
            ),
            _AmountField(
              controller: row.unitPrice,
              label: 'فی',
              width: 150,
              onChanged: onChanged,
            ),
            _AmountField(
              controller: row.discount,
              label: 'تخفیف',
              width: 130,
              onChanged: onChanged,
            ),
            _AmountField(
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

class _AmountField extends StatelessWidget {
  const _AmountField({
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
            const TextInputType.numberWithOptions(
          decimal: true,
        ),
        textDirection: TextDirection.ltr,
        onChanged: (_) => onChanged(),
        decoration: InputDecoration(
          labelText: label,
        ),
      ),
    );
  }
}

class _InvoiceRowEditor {
  String? productId;
  final quantity = TextEditingController(text: '1');
  final unitPrice = TextEditingController();
  final discount = TextEditingController(text: '0');
  final tax = TextEditingController(text: '0');
  final lot = TextEditingController();
  final serial = TextEditingController();
  DateTime? expiryDate;

  void dispose() {
    quantity.dispose();
    unitPrice.dispose();
    discount.dispose();
    tax.dispose();
    lot.dispose();
    serial.dispose();
  }
}
