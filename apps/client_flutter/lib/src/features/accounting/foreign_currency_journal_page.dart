import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import 'report_support.dart';

class ForeignCurrencyJournalPage extends StatefulWidget {
  const ForeignCurrencyJournalPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<ForeignCurrencyJournalPage> createState() =>
      _ForeignCurrencyJournalPageState();
}

class _ForeignCurrencyJournalPageState
    extends State<ForeignCurrencyJournalPage> {
  final _apiClient = ApiClient();
  final _description = TextEditingController();
  final _rate = TextEditingController();
  final List<_FxRowEditor> _rows = [];

  List<Map<String, dynamic>> _currencies = const [];
  List<CachedAccount> _accounts = const [];
  List<CachedDetailAccount> _details = const [];
  List<CachedFiscalYear> _fiscalYears = const [];

  String? _currencyId;
  String? _fiscalYearId;
  DateTime _documentDate = DateTime.now();
  bool _loading = true;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _rows.add(_FxRowEditor());
    _rows.add(_FxRowEditor());
    _load();
  }

  @override
  void dispose() {
    _description.dispose();
    _rate.dispose();
    for (final row in _rows) {
      row.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final currencies = await _apiClient.getCurrencies(
        bearerToken: widget.accessToken,
      );
      final accounts =
          await widget.localDatabase.getCachedAccounts(widget.companyId);
      final details =
          await widget.localDatabase.getCachedDetailAccounts(widget.companyId);
      final years =
          await widget.localDatabase.getCachedFiscalYears(widget.companyId);

      String? currencyId;
      for (final currency in currencies) {
        if ((currency['isActive'] as bool? ?? true) &&
            !(currency['isBase'] as bool? ?? false)) {
          currencyId = currency['id'].toString();
          break;
        }
      }

      String? fiscalYearId;
      for (final year in years) {
        if (!year.isClosed && year.contains(_documentDate)) {
          fiscalYearId = year.id;
          if (year.isDefault) break;
        }
      }

      if (!mounted) return;

      setState(() {
        _currencies = currencies;
        _accounts = accounts
            .where((x) => x.isActive && x.isPostable)
            .toList(growable: false);
        _details = details
            .where((x) => x.isActive)
            .toList(growable: false);
        _fiscalYears = years;
        _currencyId = currencyId;
        _fiscalYearId = fiscalYearId;
        _loading = false;
      });

      await _loadRate();
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.toString();
        _loading = false;
      });
    }
  }

  Map<String, dynamic>? _selectedCurrency() {
    for (final item in _currencies) {
      if (item['id'].toString() == _currencyId) return item;
    }
    return null;
  }

  num _parse(String value) {
    return num.tryParse(
          value.replaceAll(',', '').trim(),
        ) ??
        0;
  }

  num get _foreignDebit {
    return _rows.fold<num>(
      0,
      (sum, row) => sum + _parse(row.debit.text),
    );
  }

  num get _foreignCredit {
    return _rows.fold<num>(
      0,
      (sum, row) => sum + _parse(row.credit.text),
    );
  }

  num get _baseTotal {
    return _foreignDebit * _parse(_rate.text);
  }

  Future<void> _loadRate() async {
    final currencyId = _currencyId;
    if (currencyId == null) return;

    try {
      final value = await _apiClient.getCurrencyAccountingRate(
        bearerToken: widget.accessToken,
        currencyId: currencyId,
        date: _documentDate,
      );

      if (!mounted) return;
      setState(() => _rate.text = value.toString());
    } on ApiException {
      if (!mounted) return;
      setState(() => _rate.clear());
    }
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

    await _loadRate();
  }

  void _addRow() {
    setState(() => _rows.add(_FxRowEditor()));
  }

  void _removeRow(int index) {
    if (_rows.length <= 2) return;
    final row = _rows.removeAt(index);
    row.dispose();
    setState(() {});
  }

  Future<void> _save() async {
    final currencyId = _currencyId;
    final fiscalYearId = _fiscalYearId;
    final exchangeRate = _parse(_rate.text);

    if (currencyId == null) {
      _message('یک ارز خارجی فعال انتخاب کنید.');
      return;
    }

    if (fiscalYearId == null) {
      _message('سال مالی باز برای تاریخ سند پیدا نشد.');
      return;
    }

    if (exchangeRate <= 0) {
      _message('نرخ حسابداری باید بزرگ‌تر از صفر باشد.');
      return;
    }

    final lines = <Map<String, dynamic>>[];

    for (final row in _rows) {
      if (row.accountId == null) continue;

      final debit = _parse(row.debit.text);
      final credit = _parse(row.credit.text);

      if ((debit <= 0 && credit <= 0) ||
          (debit > 0 && credit > 0)) {
        _message(
          'در هر ردیف فقط یکی از بدهکار یا بستانکار ارزی باید مبلغ داشته باشد.',
        );
        return;
      }

      lines.add({
        'accountId': row.accountId,
        'description': row.description.text.trim(),
        'foreignDebit': debit,
        'foreignCredit': credit,
        'detailAccountId': row.detailAccountId,
      });
    }

    if (lines.length < 2) {
      _message('حداقل دو ردیف حسابداری لازم است.');
      return;
    }

    if (_foreignDebit <= 0 || _foreignDebit != _foreignCredit) {
      _message('جمع بدهکار و بستانکار ارزی باید برابر باشد.');
      return;
    }

    setState(() => _saving = true);

    try {
      final response =
          await _apiClient.createForeignCurrencyJournal(
        bearerToken: widget.accessToken,
        currencyId: currencyId,
        documentDate: _documentDate,
        description: _description.text.trim(),
        lines: lines,
        fiscalYearId: fiscalYearId,
        exchangeRate: exchangeRate,
      );

      if (!mounted) return;

      final number = response['number'].toString();
      _message(
        'سند ارزی با شماره ' +
            number +
            ' ثبت شد. نرخ: ' +
            response['exchangeRate'].toString(),
      );

      Navigator.pop(context, response);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(text)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final currency = _selectedCurrency();
    final foreignCurrencies = _currencies
        .where(
          (x) =>
              (x['isActive'] as bool? ?? true) &&
              !(x['isBase'] as bool? ?? false),
        )
        .toList(growable: false);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('ثبت سند ارزی'),
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
                          padding: const EdgeInsets.all(16),
                          child: Wrap(
                            spacing: 12,
                            runSpacing: 12,
                            children: [
                              SizedBox(
                                width: 230,
                                child: DropdownButtonFormField<String>(
                                  initialValue: _currencyId,
                                  isExpanded: true,
                                  decoration: const InputDecoration(
                                    labelText: 'ارز',
                                    prefixIcon: Icon(
                                      Icons.currency_exchange,
                                    ),
                                  ),
                                  items: [
                                    for (final item in foreignCurrencies)
                                      DropdownMenuItem(
                                        value: item['id'].toString(),
                                        child: Text(
                                          item['code'].toString() +
                                              ' — ' +
                                              item['name'].toString(),
                                        ),
                                      ),
                                  ],
                                  onChanged: _saving
                                      ? null
                                      : (value) async {
                                          setState(
                                            () => _currencyId = value,
                                          );
                                          await _loadRate();
                                        },
                                ),
                              ),
                              SizedBox(
                                width: 220,
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
                                      : (value) {
                                          setState(
                                            () => _fiscalYearId = value,
                                          );
                                        },
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
                                child: TextField(
                                  controller: _rate,
                                  enabled: !_saving,
                                  textDirection: TextDirection.ltr,
                                  keyboardType:
                                      const TextInputType.numberWithOptions(
                                    decimal: true,
                                  ),
                                  onChanged: (_) => setState(() {}),
                                  decoration: InputDecoration(
                                    labelText: 'نرخ حسابداری',
                                    helperText: currency == null
                                        ? null
                                        : 'ارز پایه برای 1 ' +
                                            currency['code'].toString(),
                                  ),
                                ),
                              ),
                              IconButton(
                                tooltip: 'دریافت نرخ ثبت‌شده',
                                onPressed: _saving ? null : _loadRate,
                                icon: const Icon(Icons.sync),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: _description,
                        decoration: const InputDecoration(
                          labelText: 'شرح سند',
                        ),
                      ),
                      const SizedBox(height: 12),
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment:
                                CrossAxisAlignment.stretch,
                            children: [
                              Row(
                                children: [
                                  const Expanded(
                                    child: Text(
                                      'ردیف‌های ارزی',
                                      style: TextStyle(
                                        fontSize: 18,
                                        fontWeight: FontWeight.w700,
                                      ),
                                    ),
                                  ),
                                  TextButton.icon(
                                    onPressed: _saving ? null : _addRow,
                                    icon: const Icon(Icons.add),
                                    label: const Text('افزودن ردیف'),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 10),
                              for (var index = 0;
                                  index < _rows.length;
                                  index++) ...[
                                _FxLineCard(
                                  row: _rows[index],
                                  accounts: _accounts,
                                  details: _details,
                                  enabled: !_saving,
                                  onChanged: () => setState(() {}),
                                  onRemove: () => _removeRow(index),
                                ),
                                if (index != _rows.length - 1)
                                  const SizedBox(height: 8),
                              ],
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Wrap(
                            spacing: 24,
                            runSpacing: 8,
                            children: [
                              Text(
                                'بدهکار ارزی: ' +
                                    formatReportMoney(_foreignDebit),
                              ),
                              Text(
                                'بستانکار ارزی: ' +
                                    formatReportMoney(_foreignCredit),
                              ),
                              Text(
                                'معادل بدهکار در ارز پایه: ' +
                                    formatReportMoney(_baseTotal),
                              ),
                              Chip(
                                avatar: Icon(
                                  _foreignDebit > 0 &&
                                          _foreignDebit == _foreignCredit
                                      ? Icons.check_circle_outline
                                      : Icons.warning_amber_outlined,
                                  size: 18,
                                ),
                                label: Text(
                                  _foreignDebit > 0 &&
                                          _foreignDebit == _foreignCredit
                                      ? 'تراز ارزی برقرار است'
                                      : 'سند ارزی نامتوازن است',
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
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(Icons.save_outlined),
                        label: const Text('ثبت قطعی سند ارزی'),
                      ),
                      const SizedBox(height: 10),
                      const Text(
                        'دفتر کل با ارز پایه نگهداری می‌شود؛ مبلغ اصلی ارز و نرخ استفاده‌شده روی هر خط سند جداگانه حفظ می‌شود.',
                        style: TextStyle(fontSize: 12),
                      ),
                    ],
                  ),
      ),
    );
  }
}

class _FxLineCard extends StatelessWidget {
  const _FxLineCard({
    required this.row,
    required this.accounts,
    required this.details,
    required this.enabled,
    required this.onChanged,
    required this.onRemove,
  });

  final _FxRowEditor row;
  final List<CachedAccount> accounts;
  final List<CachedDetailAccount> details;
  final bool enabled;
  final VoidCallback onChanged;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Wrap(
          spacing: 10,
          runSpacing: 10,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 300,
              child: DropdownButtonFormField<String>(
                initialValue: row.accountId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'حساب',
                ),
                items: [
                  for (final account in accounts)
                    DropdownMenuItem(
                      value: account.id,
                      child: Text(
                        account.code + ' — ' + account.name,
                      ),
                    ),
                ],
                onChanged: enabled
                    ? (value) {
                        row.accountId = value;
                        onChanged();
                      }
                    : null,
              ),
            ),
            SizedBox(
              width: 270,
              child: DropdownButtonFormField<String>(
                initialValue: row.detailAccountId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'تفصیلی',
                ),
                items: [
                  const DropdownMenuItem<String>(
                    value: null,
                    child: Text('بدون تفصیلی'),
                  ),
                  for (final detail in details)
                    DropdownMenuItem(
                      value: detail.id,
                      child: Text(
                        detail.code + ' — ' + detail.name,
                      ),
                    ),
                ],
                onChanged: enabled
                    ? (value) {
                        row.detailAccountId = value;
                        onChanged();
                      }
                    : null,
              ),
            ),
            _FxAmountField(
              controller: row.debit,
              label: 'بدهکار ارزی',
              enabled: enabled,
              onChanged: onChanged,
            ),
            _FxAmountField(
              controller: row.credit,
              label: 'بستانکار ارزی',
              enabled: enabled,
              onChanged: onChanged,
            ),
            SizedBox(
              width: 260,
              child: TextField(
                controller: row.description,
                enabled: enabled,
                decoration: const InputDecoration(
                  labelText: 'شرح ردیف',
                ),
              ),
            ),
            IconButton(
              tooltip: 'حذف ردیف',
              onPressed: enabled ? onRemove : null,
              icon: const Icon(Icons.delete_outline),
            ),
          ],
        ),
      ),
    );
  }
}

class _FxAmountField extends StatelessWidget {
  const _FxAmountField({
    required this.controller,
    required this.label,
    required this.enabled,
    required this.onChanged,
  });

  final TextEditingController controller;
  final String label;
  final bool enabled;
  final VoidCallback onChanged;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 150,
      child: TextField(
        controller: controller,
        enabled: enabled,
        textDirection: TextDirection.ltr,
        keyboardType: const TextInputType.numberWithOptions(
          decimal: true,
        ),
        onChanged: (_) => onChanged(),
        decoration: InputDecoration(labelText: label),
      ),
    );
  }
}

class _FxRowEditor {
  String? accountId;
  String? detailAccountId;
  final debit = TextEditingController();
  final credit = TextEditingController();
  final description = TextEditingController();

  void dispose() {
    debit.dispose();
    credit.dispose();
    description.dispose();
  }
}
