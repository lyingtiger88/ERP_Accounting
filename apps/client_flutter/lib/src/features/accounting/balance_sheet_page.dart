import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import 'report_support.dart';

class BalanceSheetPage extends StatefulWidget {
  const BalanceSheetPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<BalanceSheetPage> createState() => _BalanceSheetPageState();
}

class _BalanceSheetPageState extends State<BalanceSheetPage> {
  final _apiClient = ApiClient();

  AccountingReportPeriod? _period;
  DateTime? _asOf;
  Map<String, dynamic>? _report;
  bool _loading = true;
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

      final now = DateTime.now();
      DateTime? asOf = period.to;

      if (period.from != null &&
          period.to != null &&
          !now.isBefore(period.from!) &&
          !now.isAfter(period.to!)) {
        asOf = now;
      }

      if (!mounted) return;
      setState(() {
        _period = period;
        _asOf = asOf ?? now;
      });

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
    final asOf = _asOf;
    if (asOf == null) return;

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final report = await _apiClient.getBalanceSheet(
        bearerToken: widget.accessToken,
        asOf: asOf,
      );

      if (!mounted) return;
      setState(() => _report = report);
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _pickAsOf() async {
    final current = _asOf ?? DateTime.now();

    final picked = await showDatePicker(
      context: context,
      initialDate: current,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );

    if (picked == null || !mounted) return;

    setState(() => _asOf = picked);
    await _loadReport();
  }

  void _selectFiscalYear(String? id) {
    final period = _period;
    if (period == null) return;

    final selected = period.selectFiscalYear(id);

    setState(() {
      _period = selected;
      _asOf = selected.to ?? DateTime.now();
    });

    _loadReport();
  }

  @override
  Widget build(BuildContext context) {
    final period = _period;
    final report = _report;

    final rows = report == null
        ? const <Map<String, dynamic>>[]
        : (report['rows'] as List<dynamic>)
            .map(
              (item) =>
                  Map<String, dynamic>.from(item as Map),
            )
            .toList(growable: false);

    List<Map<String, dynamic>> section(String value) {
      return rows
          .where((row) => row['section'].toString() == value)
          .toList(growable: false);
    }

    final assets = section('Asset');
    final liabilities = section('Liability');
    final equity = section('Equity');

    final assetTotal =
        reportNumber(report?['assetTotal']);
    final liabilityTotal =
        reportNumber(report?['liabilityTotal']);
    final equityTotal =
        reportNumber(report?['equityTotal']);
    final accumulatedResult =
        reportNumber(report?['accumulatedResult']);
    final rightSideTotal =
        reportNumber(report?['rightSideTotal']);
    final difference =
        reportNumber(report?['difference']);

    final balanced = difference.abs() < 0.0001;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('ترازنامه'),
        ),
        body: period == null
            ? const Center(child: CircularProgressIndicator())
            : ListView(
                padding: const EdgeInsets.all(20),
                children: [
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Wrap(
                        spacing: 12,
                        runSpacing: 12,
                        crossAxisAlignment:
                            WrapCrossAlignment.center,
                        children: [
                          SizedBox(
                            width: 240,
                            child:
                                DropdownButtonFormField<String>(
                              initialValue:
                                  period.selectedFiscalYearId,
                              isExpanded: true,
                              decoration: const InputDecoration(
                                labelText: 'سال مالی',
                                prefixIcon: Icon(
                                  Icons.calendar_month_outlined,
                                ),
                              ),
                              items: [
                                for (final year
                                    in period.fiscalYears)
                                  DropdownMenuItem(
                                    value: year.id,
                                    child: Text(
                                      year.name +
                                          (year.isClosed
                                              ? ' (بسته)'
                                              : ''),
                                    ),
                                  ),
                              ],
                              onChanged: _loading
                                  ? null
                                  : _selectFiscalYear,
                            ),
                          ),
                          OutlinedButton.icon(
                            onPressed:
                                _loading ? null : _pickAsOf,
                            icon: const Icon(
                              Icons.event_available_outlined,
                            ),
                            label: Text(
                              'تا تاریخ: ' +
                                  formatReportDate(_asOf),
                            ),
                          ),
                          FilledButton.icon(
                            onPressed:
                                _loading ? null : _loadReport,
                            icon: _loading
                                ? const SizedBox(
                                    width: 18,
                                    height: 18,
                                    child:
                                        CircularProgressIndicator(
                                      strokeWidth: 2,
                                    ),
                                  )
                                : const Icon(Icons.refresh),
                            label: const Text(
                              'به‌روزرسانی ترازنامه',
                            ),
                          ),
                        ],
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
                            const Text('خطا در دریافت ترازنامه'),
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
                        padding: const EdgeInsets.all(18),
                        child: Wrap(
                          spacing: 22,
                          runSpacing: 10,
                          crossAxisAlignment:
                              WrapCrossAlignment.center,
                          children: [
                            Text(
                              'دارایی: ' +
                                  formatReportMoney(
                                    assetTotal,
                                  ) +
                                  ' ریال',
                            ),
                            Text(
                              'بدهی: ' +
                                  formatReportMoney(
                                    liabilityTotal,
                                  ) +
                                  ' ریال',
                            ),
                            Text(
                              'حقوق مالکانه: ' +
                                  formatReportMoney(
                                    equityTotal,
                                  ) +
                                  ' ریال',
                            ),
                            Text(
                              'نتیجه انباشته محاسباتی: ' +
                                  formatReportMoney(
                                    accumulatedResult,
                                  ) +
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
                                    ? 'معادله حسابداری برقرار است'
                                    : 'اختلاف: ' +
                                        formatReportMoney(
                                          difference.abs(),
                                        ) +
                                        ' ریال',
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 14),
                    _BalanceSection(
                      title: 'دارایی‌ها',
                      rows: assets,
                    ),
                    const SizedBox(height: 12),
                    _BalanceSection(
                      title: 'بدهی‌ها',
                      rows: liabilities,
                    ),
                    const SizedBox(height: 12),
                    _BalanceSection(
                      title: 'حقوق مالکانه',
                      rows: equity,
                    ),
                    const SizedBox(height: 12),
                    Card(
                      child: ListTile(
                        title: const Text(
                          'سود/زیان انباشته محاسباتی تا تاریخ گزارش',
                        ),
                        trailing: Text(
                          formatReportMoney(
                                accumulatedResult,
                              ) +
                              ' ریال',
                        ),
                      ),
                    ),
                    const SizedBox(height: 8),
                    Align(
                      alignment: Alignment.centerLeft,
                      child: Text(
                        'جمع سمت بدهی و حقوق مالکانه: ' +
                            formatReportMoney(
                              rightSideTotal,
                            ) +
                            ' ریال',
                      ),
                    ),
                  ],
                ],
              ),
      ),
    );
  }
}

class _BalanceSection extends StatelessWidget {
  const _BalanceSection({
    required this.title,
    required this.rows,
  });

  final String title;
  final List<Map<String, dynamic>> rows;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: ExpansionTile(
        initiallyExpanded: true,
        title: Text(title),
        subtitle: Text(rows.length.toString() + ' حساب'),
        children: rows.isEmpty
            ? const [
                Padding(
                  padding: EdgeInsets.all(18),
                  child: Text('مانده‌ای ثبت نشده است.'),
                ),
              ]
            : [
                for (final row in rows)
                  ListTile(
                    dense: true,
                    title: Text(
                      row['accountCode'].toString() +
                          ' — ' +
                          row['accountName'].toString(),
                    ),
                    trailing: Text(
                      formatReportMoney(
                            reportNumber(row['amount']),
                          ) +
                          ' ریال',
                    ),
                  ),
              ],
      ),
    );
  }
}
