import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';

class OpeningBalancesPage extends StatefulWidget {
  const OpeningBalancesPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<OpeningBalancesPage> createState() => _OpeningBalancesPageState();
}

class _OpeningBalancesPageState extends State<OpeningBalancesPage> {
  final _api = ApiClient();
  final _description = TextEditingController(text: 'سند افتتاحیه');
  final _rows = <_OpeningLine>[
    _OpeningLine(),
    _OpeningLine(),
  ];

  List<Map<String, dynamic>> _years = const [];
  List<Map<String, dynamic>> _accounts = const [];
  List<Map<String, dynamic>> _details = const [];
  String? _yearId;
  Map<String, dynamic>? _existing;
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
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final results = await Future.wait([
        _api.getFiscalYears(bearerToken: widget.accessToken),
        _api.getAccounts(bearerToken: widget.accessToken),
        _api.getDetailAccounts(bearerToken: widget.accessToken),
      ]);
      final years = results[0];
      final accounts = results[1]
          .where((x) => x['isActive'] == true && x['isPostable'] == true)
          .toList();
      final details = results[2].where((x) => x['isActive'] == true).toList();
      final selected = years.where((x) => x['isDefault'] == true).firstOrNull ??
          (years.isEmpty ? null : years.first);
      if (!mounted) return;
      setState(() {
        _years = years;
        _accounts = accounts;
        _details = details;
        _yearId = selected?['id'] as String?;
      });
      await _loadExisting();
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _loadExisting() async {
    final id = _yearId;
    if (id == null) return;
    try {
      final value = await _api.getOpeningBalance(
        bearerToken: widget.accessToken,
        fiscalYearId: id,
      );
      if (mounted) setState(() => _existing = value);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _selectYear(String? value) async {
    if (value == null) return;
    setState(() {
      _yearId = value;
      _existing = null;
      _error = null;
    });
    await _loadExisting();
  }

  void _addLine() {
    setState(() => _rows.add(_OpeningLine()));
  }

  void _removeLine(int index) {
    if (_rows.length <= 2) return;
    final row = _rows.removeAt(index);
    row.dispose();
    setState(() {});
  }

  num _amount(TextEditingController controller) {
    final normalized = controller.text.replaceAll(',', '').trim();
    return num.tryParse(normalized) ?? 0;
  }

  num get _debitTotal =>
      _rows.fold<num>(0, (sum, row) => sum + _amount(row.debit));
  num get _creditTotal =>
      _rows.fold<num>(0, (sum, row) => sum + _amount(row.credit));

  Future<void> _submit() async {
    if (_yearId == null) {
      setState(() => _error = 'ابتدا سال مالی را انتخاب کنید.');
      return;
    }
    if (_existing != null) {
      setState(() => _error = 'برای این سال مالی سند افتتاحیه قبلاً ثبت شده است.');
      return;
    }
    final lines = <Map<String, dynamic>>[];
    for (var i = 0; i < _rows.length; i++) {
      final row = _rows[i];
      final debit = _amount(row.debit);
      final credit = _amount(row.credit);
      if (row.accountId == null) {
        setState(() => _error = 'حساب ردیف ${i + 1} را انتخاب کنید.');
        return;
      }
      if ((debit <= 0 && credit <= 0) ||
          (debit > 0 && credit > 0) ||
          debit < 0 || credit < 0) {
        setState(() => _error =
            'در ردیف ${i + 1} فقط یکی از بدهکار یا بستانکار باید مبلغ مثبت داشته باشد.');
        return;
      }
      lines.add({
        'accountId': row.accountId,
        'description': row.description.text.trim(),
        'debit': debit,
        'credit': credit,
        'detailAccountId': row.detailAccountId,
      });
    }
    if (lines.length < 2 || _debitTotal <= 0 ||
        _debitTotal != _creditTotal) {
      setState(() => _error =
          'سند افتتاحیه باید حداقل دو ردیف داشته باشد و جمع بدهکار و بستانکار برابر باشد.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final result = await _api.createOpeningBalance(
        bearerToken: widget.accessToken,
        fiscalYearId: _yearId!,
        description: _description.text.trim(),
        lines: lines,
      );
      if (!mounted) return;
      setState(() => _existing = result);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('سند افتتاحیه با شماره ${result['journalNumber']} ثبت شد.')),
      );
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  String _money(num? value) =>
      (value ?? 0).toString().replaceAllMapped(
        RegExp(r'\B(?=(\d{3})+(?!\d))'),
        (match) => ',',
      );

  @override
  Widget build(BuildContext context) {
    final selectedYear = _years.where((x) => x['id'] == _yearId).firstOrNull;
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('مانده‌های افتتاحیه'),
          actions: [
            IconButton(
              tooltip: 'بارگذاری مجدد',
              onPressed: _loading ? null : _load,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: _loading
            ? const Center(child: CircularProgressIndicator())
            : ListView(
                padding: const EdgeInsets.all(20),
                children: [
                  const Text(
                    'ثبت مانده اول دوره',
                    style: TextStyle(fontSize: 21, fontWeight: FontWeight.bold),
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'مانده حساب‌های کل، معین و تفصیلی را در قالب یک سند افتتاحیه متوازن ثبت کنید. این عملیات برای هر سال مالی فقط یک‌بار مجاز است.',
                  ),
                  const SizedBox(height: 18),
                  DropdownButtonFormField<String>(
                    value: _yearId,
                    decoration: const InputDecoration(
                      labelText: 'سال مالی',
                      border: OutlineInputBorder(),
                    ),
                    items: _years.map((year) {
                      return DropdownMenuItem<String>(
                        value: year['id'] as String,
                        child: Text(year['name']?.toString() ?? ''),
                      );
                    }).toList(),
                    onChanged: _existing == null ? _selectYear : null,
                  ),
                  if (selectedYear != null) ...[
                    const SizedBox(height: 8),
                    Text('تاریخ شروع سال مالی: ${selectedYear['startDate'] ?? '-'}'),
                  ],
                  const SizedBox(height: 16),
                  if (_error != null) ...[
                    Material(
                      color: Theme.of(context).colorScheme.errorContainer,
                      borderRadius: BorderRadius.circular(8),
                      child: Padding(
                        padding: const EdgeInsets.all(12),
                        child: Text(_error!),
                      ),
                    ),
                    const SizedBox(height: 12),
                  ],
                  if (_existing != null)
                    Card(
                      child: ListTile(
                        leading: const Icon(Icons.verified_outlined),
                        title: const Text('سند افتتاحیه ثبت شده است'),
                        subtitle: Text(
                          'شماره سند: ${_existing!['journalNumber']}\n'
                          'بدهکار: ${_money(_existing!['totalDebit'] as num?)}  |  '
                          'بستانکار: ${_money(_existing!['totalCredit'] as num?)}',
                        ),
                      ),
                    )
                  else ...[
                    TextField(
                      controller: _description,
                      decoration: const InputDecoration(
                        labelText: 'شرح سند',
                        border: OutlineInputBorder(),
                      ),
                    ),
                    const SizedBox(height: 14),
                    ...List.generate(_rows.length, (index) {
                      final row = _rows[index];
                      return Card(
                        margin: const EdgeInsets.only(bottom: 12),
                        child: Padding(
                          padding: const EdgeInsets.all(12),
                          child: Column(
                            children: [
                              Row(
                                children: [
                                  Expanded(
                                    child: Text('ردیف ${index + 1}',
                                        style: const TextStyle(fontWeight: FontWeight.bold)),
                                  ),
                                  if (_rows.length > 2)
                                    IconButton(
                                      onPressed: () => _removeLine(index),
                                      icon: const Icon(Icons.delete_outline),
                                    ),
                                ],
                              ),
                              DropdownButtonFormField<String>(
                                value: row.accountId,
                                isExpanded: true,
                                decoration: const InputDecoration(
                                  labelText: 'حساب قابل ثبت',
                                  border: OutlineInputBorder(),
                                ),
                                items: _accounts.map((account) {
                                  return DropdownMenuItem<String>(
                                    value: account['id'] as String,
                                    child: Text('${account['code']} - ${account['name']}'),
                                  );
                                }).toList(),
                                onChanged: (value) => setState(() => row.accountId = value),
                              ),
                              const SizedBox(height: 10),
                              DropdownButtonFormField<String>(
                                value: row.detailAccountId,
                                isExpanded: true,
                                decoration: const InputDecoration(
                                  labelText: 'تفصیلی (اختیاری)',
                                  border: OutlineInputBorder(),
                                ),
                                items: [
                                  const DropdownMenuItem<String>(
                                    value: null,
                                    child: Text('بدون تفصیلی'),
                                  ),
                                  ..._details.map((detail) => DropdownMenuItem<String>(
                                    value: detail['id'] as String,
                                    child: Text('${detail['code']} - ${detail['name']}'),
                                  )),
                                ],
                                onChanged: (value) => setState(() => row.detailAccountId = value),
                              ),
                              const SizedBox(height: 10),
                              TextField(
                                controller: row.description,
                                decoration: const InputDecoration(
                                  labelText: 'شرح ردیف',
                                  border: OutlineInputBorder(),
                                ),
                              ),
                              const SizedBox(height: 10),
                              Row(
                                children: [
                                  Expanded(
                                    child: TextField(
                                      controller: row.debit,
                                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                                      onChanged: (_) => setState(() {}),
                                      decoration: const InputDecoration(
                                        labelText: 'بدهکار',
                                        border: OutlineInputBorder(),
                                      ),
                                    ),
                                  ),
                                  const SizedBox(width: 10),
                                  Expanded(
                                    child: TextField(
                                      controller: row.credit,
                                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                                      onChanged: (_) => setState(() {}),
                                      decoration: const InputDecoration(
                                        labelText: 'بستانکار',
                                        border: OutlineInputBorder(),
                                      ),
                                    ),
                                  ),
                                ],
                              ),
                            ],
                          ),
                        ),
                      );
                    }),
                    OutlinedButton.icon(
                      onPressed: _addLine,
                      icon: const Icon(Icons.add),
                      label: const Text('افزودن ردیف'),
                    ),
                    const SizedBox(height: 14),
                    Card(
                      child: Padding(
                        padding: const EdgeInsets.all(14),
                        child: Column(
                          children: [
                            _TotalRow(label: 'جمع بدهکار', value: _money(_debitTotal)),
                            _TotalRow(label: 'جمع بستانکار', value: _money(_creditTotal)),
                            const Divider(),
                            _TotalRow(
                              label: 'اختلاف',
                              value: _money((_debitTotal - _creditTotal).abs()),
                              bold: true,
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 14),
                    FilledButton.icon(
                      onPressed: _saving || _accounts.isEmpty || _years.isEmpty
                          ? null
                          : _submit,
                      icon: _saving
                          ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                          : const Icon(Icons.lock_open_outlined),
                      label: const Text('ثبت قطعی سند افتتاحیه'),
                    ),
                  ],
                ],
              ),
      ),
    );
  }
}

class _OpeningLine {
  String? accountId;
  String? detailAccountId;
  final description = TextEditingController();
  final debit = TextEditingController();
  final credit = TextEditingController();

  void dispose() {
    description.dispose();
    debit.dispose();
    credit.dispose();
  }
}

class _TotalRow extends StatelessWidget {
  const _TotalRow({
    required this.label,
    required this.value,
    this.bold = false,
  });

  final String label;
  final String value;
  final bool bold;

  @override
  Widget build(BuildContext context) {
    final style = TextStyle(fontWeight: bold ? FontWeight.bold : FontWeight.normal);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label, style: style),
          Text(value, style: style),
        ],
      ),
    );
  }
}
