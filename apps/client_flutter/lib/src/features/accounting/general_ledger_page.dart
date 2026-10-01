import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import 'report_support.dart';

class GeneralLedgerPage extends StatefulWidget {
  const GeneralLedgerPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<GeneralLedgerPage> createState() => _GeneralLedgerPageState();
}

class _GeneralLedgerPageState extends State<GeneralLedgerPage> {
  final _apiClient = ApiClient();

  AccountingReportPeriod? _period;
  List<CachedAccount> _accounts = const [];
  String? _accountId;
  List<Map<String, dynamic>> _rows = const [];
  num _openingBalance = 0;
  num _debitTurnover = 0;
  num _creditTurnover = 0;
  num _closingBalance = 0;
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
        _accounts = accounts
            .where(
              (account) =>
                  account.isActive && account.isPostable,
            )
            .toList(growable: false);
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
      final report = await _apiClient.getGeneralLedger(
        bearerToken: widget.accessToken,
        accountId: _accountId,
        from: period.from,
        to: period.to,
      );

      final rows = (report['rows'] as List<dynamic>)
          .map(
            (item) => Map<String, dynamic>.from(item as Map),
          )
          .toList(growable: false);

      if (!mounted) return;
      setState(() {
        _rows = rows;
        _openingBalance =
            reportNumber(report['openingBalance']);
        _debitTurnover =
            reportNumber(report['debitTurnover']);
        _creditTurnover =
            reportNumber(report['creditTurnover']);
        _closingBalance =
            reportNumber(report['closingBalance']);
      });
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
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('دفتر کل و معین'),
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
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Row(
                        children: [
                          Expanded(
                            child: DropdownButtonFormField<String>(
                              initialValue: _accountId,
                              isExpanded: true,
                              decoration: const InputDecoration(
                                labelText: 'حساب',
                                prefixIcon: Icon(
                                  Icons.account_tree_outlined,
                                ),
                              ),
                              items: [
                                const DropdownMenuItem<String>(
                                  value: null,
                                  child: Text('همه حساب‌ها'),
                                ),
                                for (final account in _accounts)
                                  DropdownMenuItem(
                                    value: account.id,
                                    child: Text(
                                      account.code +
                                          ' — ' +
                                          account.name,
                                    ),
                                  ),
                              ],
                              onChanged: _loading
                                  ? null
                                  : (value) async {
                                      setState(
                                        () => _accountId = value,
                                      );
                                      await _loadReport();
                                    },
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
                          color: Theme.of(context).colorScheme.error,
                        ),
                        title: const Text('خطا در دریافت دفتر کل'),
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
                          spacing: 28,
                          runSpacing: 10,
                          children: [
                            if (_accountId != null)
                              Text(
                                'مانده افتتاحیه: ' +
                                    formatReportMoney(
                                      _openingBalance,
                                    ) +
                                    ' ریال',
                              ),
                            Text(
                              'گردش بدهکار: ' +
                                  formatReportMoney(
                                    _debitTurnover,
                                  ) +
                                  ' ریال',
                            ),
                            Text(
                              'گردش بستانکار: ' +
                                  formatReportMoney(
                                    _creditTurnover,
                                  ) +
                                  ' ریال',
                            ),
                            if (_accountId != null)
                              Text(
                                'مانده پایان: ' +
                                    formatReportMoney(
                                      _closingBalance,
                                    ) +
                                    ' ریال',
                              ),
                            Text(
                              'تعداد گردش‌ها: ' +
                                  _rows.length.toString(),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    if (_rows.isEmpty)
                      const Padding(
                        padding: EdgeInsets.all(40),
                        child: Center(
                          child: Text(
                            'برای فیلتر انتخاب‌شده گردش حسابی وجود ندارد.',
                          ),
                        ),
                      )
                    else
                      _LedgerTable(rows: _rows),
                  ],
                ],
              ),
      ),
    );
  }
}

class _LedgerTable extends StatelessWidget {
  const _LedgerTable({required this.rows});

  final List<Map<String, dynamic>> rows;

  @override
  Widget build(BuildContext context) {
    return Card(
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
              label: Text('مانده جاری'),
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
                          row['documentDate'].toString(),
                        ),
                      ),
                    ),
                  ),
                  DataCell(Text(row['journalNumber'].toString())),
                  DataCell(
                    Text(
                      row['accountCode'].toString() +
                          ' — ' +
                          row['accountName'].toString(),
                    ),
                  ),
                  DataCell(
                    Text(row['description']?.toString() ?? ''),
                  ),
                  DataCell(
                    Text(
                      formatReportMoney(
                        reportNumber(row['debit']),
                      ),
                    ),
                  ),
                  DataCell(
                    Text(
                      formatReportMoney(
                        reportNumber(row['credit']),
                      ),
                    ),
                  ),
                  DataCell(
                    Text(
                      formatReportMoney(
                        reportNumber(row['runningBalance']),
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
