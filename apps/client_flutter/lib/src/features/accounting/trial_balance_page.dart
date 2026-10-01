import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import 'report_support.dart';

class TrialBalancePage extends StatefulWidget {
  const TrialBalancePage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<TrialBalancePage> createState() => _TrialBalancePageState();
}

class _TrialBalancePageState extends State<TrialBalancePage> {
  final _apiClient = ApiClient();

  AccountingReportPeriod? _period;
  List<Map<String, dynamic>> _rows = const [];
  bool _loading = true;
  bool _hideZero = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _initialize();
  }

  Future<void> _initialize() async {
    try {
      final period = await AccountingReportPeriod.load(
        localDatabase: widget.localDatabase,
        companyId: widget.companyId,
      );

      if (!mounted) return;
      setState(() => _period = period);

      await _loadReport();
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.toString();
        _loading = false;
      });
    }
  }

  Future<void> _loadReport() async {
    final period = _period;
    if (period == null) return;

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final rows = await _apiClient.getTrialBalance(
        bearerToken: widget.accessToken,
        from: period.from,
        to: period.to,
      );

      if (!mounted) return;
      setState(() => _rows = rows);
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _pickDate({required bool from}) async {
    final period = _period;
    if (period == null) return;

    final picked = await showDatePicker(
      context: context,
      initialDate: from
          ? (period.from ?? DateTime.now())
          : (period.to ?? DateTime.now()),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );

    if (picked == null || !mounted) return;

    setState(() {
      _period = from
          ? period.copyWith(from: picked)
          : period.copyWith(to: picked);
    });

    await _loadReport();
  }

  @override
  Widget build(BuildContext context) {
    final period = _period;

    final visibleRows = _hideZero
        ? _rows.where((row) {
            final debit = reportNumber(row['debitTurnover']);
            final credit = reportNumber(row['creditTurnover']);
            final balance = reportNumber(row['balance']);
            return debit != 0 || credit != 0 || balance != 0;
          }).toList(growable: false)
        : _rows;

    final totalDebit = visibleRows.fold<num>(
      0,
      (sum, row) =>
          sum + reportNumber(row['debitTurnover']),
    );
    final totalCredit = visibleRows.fold<num>(
      0,
      (sum, row) =>
          sum + reportNumber(row['creditTurnover']),
    );
    final balanced = totalDebit == totalCredit;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('تراز آزمایشی'),
        ),
        body: period == null
            ? const Center(child: CircularProgressIndicator())
            : ListView(
                padding: const EdgeInsets.all(20),
                children: [
                  AccountingReportFilterBar(
                    period: period,
                    loading: _loading,
                    onFiscalYearChanged: (value) async {
                      setState(() {
                        _period = period.selectFiscalYear(value);
                      });
                      await _loadReport();
                    },
                    onPickFrom: () => _pickDate(from: true),
                    onPickTo: () => _pickDate(from: false),
                    onRefresh: _loadReport,
                  ),
                  const SizedBox(height: 12),
                  Card(
                    child: SwitchListTile(
                      title: const Text(
                        'پنهان‌کردن حساب‌های بدون گردش',
                      ),
                      subtitle: Text(
                        _hideZero
                            ? 'فقط حساب‌های دارای گردش نمایش داده می‌شوند.'
                            : 'همه حساب‌ها نمایش داده می‌شوند.',
                      ),
                      value: _hideZero,
                      onChanged: (value) {
                        setState(() => _hideZero = value);
                      },
                    ),
                  ),
                  const SizedBox(height: 12),
                  if (_error != null)
                    Card(
                      child: ListTile(
                        leading: Icon(
                          Icons.error_outline,
                          color: Theme.of(context).colorScheme.error,
                        ),
                        title: const Text('خطا در دریافت تراز آزمایشی'),
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
                  else ...[
                    Card(
                      child: Padding(
                        padding: const EdgeInsets.all(16),
                        child: Wrap(
                          spacing: 24,
                          runSpacing: 10,
                          crossAxisAlignment:
                              WrapCrossAlignment.center,
                          children: [
                            Text(
                              'جمع بدهکار: ' +
                                  formatReportMoney(totalDebit) +
                                  ' ریال',
                            ),
                            Text(
                              'جمع بستانکار: ' +
                                  formatReportMoney(totalCredit) +
                                  ' ریال',
                            ),
                            Chip(
                              avatar: Icon(
                                balanced
                                    ? Icons.check_circle_outline
                                    : Icons.warning_amber_outlined,
                                size: 18,
                              ),
                              label: Text(
                                balanced
                                    ? 'تراز برقرار است'
                                    : 'اختلاف: ' +
                                        formatReportMoney(
                                          (totalDebit -
                                                  totalCredit)
                                              .abs(),
                                        ) +
                                        ' ریال',
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    if (visibleRows.isEmpty)
                      const Padding(
                        padding: EdgeInsets.all(40),
                        child: Center(
                          child: Text(
                            'برای این بازه گردش حسابی وجود ندارد.',
                          ),
                        ),
                      )
                    else
                      _TrialBalanceTable(rows: visibleRows),
                  ],
                ],
              ),
      ),
    );
  }
}

class _TrialBalanceTable extends StatelessWidget {
  const _TrialBalanceTable({required this.rows});

  final List<Map<String, dynamic>> rows;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: DataTable(
          columns: const [
            DataColumn(label: Text('کد حساب')),
            DataColumn(label: Text('نام حساب')),
            DataColumn(
              numeric: true,
              label: Text('گردش بدهکار'),
            ),
            DataColumn(
              numeric: true,
              label: Text('گردش بستانکار'),
            ),
            DataColumn(
              numeric: true,
              label: Text('مانده'),
            ),
          ],
          rows: [
            for (final row in rows)
              DataRow(
                cells: [
                  DataCell(Text(row['accountCode'].toString())),
                  DataCell(Text(row['accountName'].toString())),
                  DataCell(
                    Text(
                      formatReportMoney(
                        reportNumber(row['debitTurnover']),
                      ),
                    ),
                  ),
                  DataCell(
                    Text(
                      formatReportMoney(
                        reportNumber(row['creditTurnover']),
                      ),
                    ),
                  ),
                  DataCell(
                    Text(
                      formatReportMoney(
                        reportNumber(row['balance']),
                      ),
                    ),
                  ),
                ],
              ),
          ],
        ),
      ),
    );
  }
}
