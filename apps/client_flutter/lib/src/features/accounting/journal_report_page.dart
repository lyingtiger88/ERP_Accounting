import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import 'report_support.dart';

class JournalReportPage extends StatefulWidget {
  const JournalReportPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<JournalReportPage> createState() => _JournalReportPageState();
}

class _JournalReportPageState extends State<JournalReportPage> {
  final _apiClient = ApiClient();

  AccountingReportPeriod? _period;
  Map<String, CachedAccount> _accounts = const {};
  List<Map<String, dynamic>> _journals = const [];
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
      final accounts =
          await widget.localDatabase.getCachedAccounts(widget.companyId);

      if (!mounted) return;

      setState(() {
        _period = period;
        _accounts = {
          for (final account in accounts) account.id: account,
        };
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
    final period = _period;
    if (period == null) return;

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final rows = await _apiClient.getJournals(
        bearerToken: widget.accessToken,
        from: period.from,
        to: period.to,
      );

      if (!mounted) return;
      setState(() => _journals = rows);
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

    final initial = from
        ? (period.from ?? DateTime.now())
        : (period.to ?? DateTime.now());

    final picked = await showDatePicker(
      context: context,
      initialDate: initial,
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

  num _lineTotal(
    List<dynamic> lines,
    String field,
  ) {
    return lines.fold<num>(
      0,
      (sum, item) =>
          sum +
          reportNumber(
            (item as Map<String, dynamic>)[field],
          ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final period = _period;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('دفتر روزنامه'),
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
                  const SizedBox(height: 16),
                  if (_error != null)
                    Card(
                      child: ListTile(
                        leading: Icon(
                          Icons.error_outline,
                          color: Theme.of(context).colorScheme.error,
                        ),
                        title: const Text('خطا در دریافت گزارش'),
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
                  else if (_journals.isEmpty)
                    const Padding(
                      padding: EdgeInsets.all(40),
                      child: Center(
                        child: Text(
                          'در بازه انتخاب‌شده سندی ثبت نشده است.',
                        ),
                      ),
                    )
                  else ...[
                    _SummaryCard(
                      count: _journals.length,
                      debit: _journals.fold<num>(
                        0,
                        (sum, journal) => sum +
                            _lineTotal(
                              journal['lines'] as List<dynamic>? ??
                                  const [],
                              'debit',
                            ),
                      ),
                      credit: _journals.fold<num>(
                        0,
                        (sum, journal) => sum +
                            _lineTotal(
                              journal['lines'] as List<dynamic>? ??
                                  const [],
                              'credit',
                            ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    for (final journal in _journals) ...[
                      _JournalCard(
                        journal: journal,
                        accounts: _accounts,
                      ),
                      const SizedBox(height: 10),
                    ],
                  ],
                ],
              ),
      ),
    );
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({
    required this.count,
    required this.debit,
    required this.credit,
  });

  final int count;
  final num debit;
  final num credit;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Wrap(
          spacing: 28,
          runSpacing: 10,
          children: [
            Text('تعداد اسناد: ' + count.toString()),
            Text(
              'جمع بدهکار: ' +
                  formatReportMoney(debit) +
                  ' ریال',
            ),
            Text(
              'جمع بستانکار: ' +
                  formatReportMoney(credit) +
                  ' ریال',
            ),
          ],
        ),
      ),
    );
  }
}

class _JournalCard extends StatelessWidget {
  const _JournalCard({
    required this.journal,
    required this.accounts,
  });

  final Map<String, dynamic> journal;
  final Map<String, CachedAccount> accounts;

  @override
  Widget build(BuildContext context) {
    final lines =
        journal['lines'] as List<dynamic>? ?? const <dynamic>[];
    final date = DateTime.parse(journal['documentDate'].toString());
    final number = journal['number']?.toString() ?? 'بدون شماره';
    final description = journal['description']?.toString() ?? '';
    final status = journal['status']?.toString() ?? '';

    final debit = lines.fold<num>(
      0,
      (sum, item) =>
          sum +
          reportNumber(
            (item as Map<String, dynamic>)['debit'],
          ),
    );
    final credit = lines.fold<num>(
      0,
      (sum, item) =>
          sum +
          reportNumber(
            (item as Map<String, dynamic>)['credit'],
          ),
    );

    return Card(
      child: ExpansionTile(
        leading: const CircleAvatar(
          child: Icon(Icons.receipt_long_outlined),
        ),
        title: Text('سند ' + number),
        subtitle: Text(
          formatReportDate(date) +
              (description.isEmpty ? '' : ' • ' + description),
        ),
        trailing: Chip(label: Text(status)),
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 0, 20, 12),
            child: Wrap(
              spacing: 24,
              runSpacing: 8,
              children: [
                Text(
                  'بدهکار: ' +
                      formatReportMoney(debit) +
                      ' ریال',
                ),
                Text(
                  'بستانکار: ' +
                      formatReportMoney(credit) +
                      ' ریال',
                ),
              ],
            ),
          ),
          for (final rawLine in lines)
            Builder(
              builder: (context) {
                final line =
                    Map<String, dynamic>.from(rawLine as Map);
                final accountId = line['accountId']?.toString() ?? '';
                final account = accounts[accountId];
                final lineDebit = reportNumber(line['debit']);
                final lineCredit = reportNumber(line['credit']);

                return ListTile(
                  dense: true,
                  title: Text(
                    account == null
                        ? 'حساب ' + accountId
                        : account.code + ' — ' + account.name,
                  ),
                  subtitle: line['description'] == null
                      ? null
                      : Text(line['description'].toString()),
                  trailing: Text(
                    lineDebit > 0
                        ? formatReportMoney(lineDebit) + ' بدهکار'
                        : formatReportMoney(lineCredit) +
                            ' بستانکار',
                  ),
                );
              },
            ),
        ],
      ),
    );
  }
}
