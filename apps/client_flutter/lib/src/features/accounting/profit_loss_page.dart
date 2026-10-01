import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import 'report_support.dart';

class ProfitLossPage extends StatefulWidget {
  const ProfitLossPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<ProfitLossPage> createState() => _ProfitLossPageState();
}

class _ProfitLossPageState extends State<ProfitLossPage> {
  final _apiClient = ApiClient();

  AccountingReportPeriod? _period;
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
      final report = await _apiClient.getProfitLoss(
        bearerToken: widget.accessToken,
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

    final revenueRows = rows
        .where((row) => row['section'].toString() == 'Revenue')
        .toList(growable: false);
    final expenseRows = rows
        .where((row) => row['section'].toString() == 'Expense')
        .toList(growable: false);

    final revenueTotal =
        reportNumber(report?['revenueTotal']);
    final expenseTotal =
        reportNumber(report?['expenseTotal']);
    final netProfit = reportNumber(report?['netProfit']);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('صورت سود و زیان'),
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
                  if (_error != null)
                    Card(
                      child: ListTile(
                        leading: Icon(
                          Icons.error_outline,
                          color:
                              Theme.of(context).colorScheme.error,
                        ),
                        title: const Text(
                          'خطا در دریافت صورت سود و زیان',
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
                        padding: const EdgeInsets.all(18),
                        child: Wrap(
                          spacing: 28,
                          runSpacing: 12,
                          crossAxisAlignment:
                              WrapCrossAlignment.center,
                          children: [
                            Text(
                              'درآمد: ' +
                                  formatReportMoney(
                                    revenueTotal,
                                  ) +
                                  ' ریال',
                            ),
                            Text(
                              'هزینه: ' +
                                  formatReportMoney(
                                    expenseTotal,
                                  ) +
                                  ' ریال',
                            ),
                            Chip(
                              avatar: Icon(
                                netProfit >= 0
                                    ? Icons.trending_up
                                    : Icons.trending_down,
                                size: 18,
                              ),
                              label: Text(
                                (netProfit >= 0
                                        ? 'سود خالص: '
                                        : 'زیان خالص: ') +
                                    formatReportMoney(
                                      netProfit.abs(),
                                    ) +
                                    ' ریال',
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 14),
                    _FinancialSection(
                      title: 'درآمدها',
                      icon: Icons.south_west_outlined,
                      rows: revenueRows,
                    ),
                    const SizedBox(height: 14),
                    _FinancialSection(
                      title: 'هزینه‌ها',
                      icon: Icons.north_east_outlined,
                      rows: expenseRows,
                    ),
                  ],
                ],
              ),
      ),
    );
  }
}

class _FinancialSection extends StatelessWidget {
  const _FinancialSection({
    required this.title,
    required this.icon,
    required this.rows,
  });

  final String title;
  final IconData icon;
  final List<Map<String, dynamic>> rows;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: ExpansionTile(
        initiallyExpanded: true,
        leading: CircleAvatar(child: Icon(icon)),
        title: Text(title),
        subtitle: Text(rows.length.toString() + ' حساب'),
        children: rows.isEmpty
            ? const [
                Padding(
                  padding: EdgeInsets.all(18),
                  child: Text('گردشی ثبت نشده است.'),
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
