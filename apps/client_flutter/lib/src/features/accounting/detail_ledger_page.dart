import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import 'report_support.dart';

class DetailLedgerPage extends StatefulWidget {
  const DetailLedgerPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<DetailLedgerPage> createState() => _DetailLedgerPageState();
}

class _DetailLedgerPageState extends State<DetailLedgerPage> {
  final _apiClient = ApiClient();

  AccountingReportPeriod? _period;
  List<CachedDetailAccount> _details = const [];
  String? _detailId;
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
      final details =
          await widget.localDatabase.getCachedDetailAccounts(
        widget.companyId,
      );

      if (!mounted) return;

      setState(() {
        _period = period;
        _details = details;
        _detailId = details.isEmpty ? null : details.first.id;
        _loading = false;
      });

      if (_detailId != null) {
        await _loadReport();
      }
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
    final detailId = _detailId;

    if (period == null || detailId == null) {
      setState(() => _report = null);
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final report = await _apiClient.getDetailLedger(
        bearerToken: widget.accessToken,
        detailAccountId: detailId,
        from: period.from,
        to: period.to,
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
    final report = _report;

    final rows = report == null
        ? const <Map<String, dynamic>>[]
        : (report['rows'] as List<dynamic>)
            .map(
              (item) =>
                  Map<String, dynamic>.from(item as Map),
            )
            .toList(growable: false);

    final opening =
        reportNumber(report?['openingBalance']);
    final debit =
        reportNumber(report?['debitTurnover']);
    final credit =
        reportNumber(report?['creditTurnover']);
    final closing =
        reportNumber(report?['closingBalance']);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('گردش تفصیلی شناور'),
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
                        _period =
                            period.selectFiscalYear(value);
                      });
                      await _loadReport();
                    },
                    onPickFrom: () => _pickDate(from: true),
                    onPickTo: () => _pickDate(from: false),
                    onRefresh: _loadReport,
                  ),
                  const SizedBox(height: 12),
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: DropdownButtonFormField<String>(
                        initialValue: _detailId,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'تفصیلی شناور',
                          prefixIcon: Icon(Icons.badge_outlined),
                        ),
                        items: [
                          for (final detail in _details)
                            DropdownMenuItem(
                              value: detail.id,
                              child: Text(
                                detail.code +
                                    ' — ' +
                                    detail.name,
                              ),
                            ),
                        ],
                        onChanged: _loading
                            ? null
                            : (value) async {
                                setState(() => _detailId = value);
                                await _loadReport();
                              },
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  if (_details.isEmpty)
                    const Card(
                      child: Padding(
                        padding: EdgeInsets.all(24),
                        child: Text(
                          'ابتدا یک تفصیلی شناور تعریف کنید.',
                        ),
                      ),
                    )
                  else if (_error != null)
                    Card(
                      child: ListTile(
                        leading: Icon(
                          Icons.error_outline,
                          color:
                              Theme.of(context).colorScheme.error,
                        ),
                        title: const Text(
                          'خطا در دریافت گردش تفصیلی',
                        ),
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
                          children: [
                            Text(
                              'مانده افتتاحیه: ' +
                                  formatReportMoney(opening) +
                                  ' ریال',
                            ),
                            Text(
                              'گردش بدهکار: ' +
                                  formatReportMoney(debit) +
                                  ' ریال',
                            ),
                            Text(
                              'گردش بستانکار: ' +
                                  formatReportMoney(credit) +
                                  ' ریال',
                            ),
                            Text(
                              'مانده پایان: ' +
                                  formatReportMoney(closing) +
                                  ' ریال',
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    if (rows.isEmpty)
                      const Padding(
                        padding: EdgeInsets.all(40),
                        child: Center(
                          child: Text(
                            'برای این تفصیلی در بازه انتخاب‌شده گردشی وجود ندارد.',
                          ),
                        ),
                      )
                    else
                      Card(
                        child: SingleChildScrollView(
                          scrollDirection: Axis.horizontal,
                          child: DataTable(
                            columns: const [
                              DataColumn(label: Text('تاریخ')),
                              DataColumn(label: Text('سند')),
                              DataColumn(label: Text('حساب')),
                              DataColumn(label: Text('شرح')),
                              DataColumn(
                                numeric: true,
                                label: Text('بدهکار'),
                              ),
                              DataColumn(
                                numeric: true,
                                label: Text('بستانکار'),
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
                                    DataCell(
                                      Text(
                                        formatReportDate(
                                          DateTime.parse(
                                            row['documentDate']
                                                .toString(),
                                          ),
                                        ),
                                      ),
                                    ),
                                    DataCell(
                                      Text(
                                        row['journalNumber']
                                            .toString(),
                                      ),
                                    ),
                                    DataCell(
                                      Text(
                                        row['accountCode']
                                                .toString() +
                                            ' — ' +
                                            row['accountName']
                                                .toString(),
                                      ),
                                    ),
                                    DataCell(
                                      Text(
                                        row['description']
                                                ?.toString() ??
                                            '',
                                      ),
                                    ),
                                    DataCell(
                                      Text(
                                        formatReportMoney(
                                          reportNumber(
                                            row['debit'],
                                          ),
                                        ),
                                      ),
                                    ),
                                    DataCell(
                                      Text(
                                        formatReportMoney(
                                          reportNumber(
                                            row['credit'],
                                          ),
                                        ),
                                      ),
                                    ),
                                    DataCell(
                                      Text(
                                        formatReportMoney(
                                          reportNumber(
                                            row['runningBalance'],
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
                ],
              ),
      ),
    );
  }
}
