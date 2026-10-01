import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/date/jalali_date.dart';

class FiscalPeriodsPage extends StatefulWidget {
  const FiscalPeriodsPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
    required this.fiscalYear,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;
  final CachedFiscalYear fiscalYear;

  @override
  State<FiscalPeriodsPage> createState() => _FiscalPeriodsPageState();
}

class _FiscalPeriodsPageState extends State<FiscalPeriodsPage> {
  final _apiClient = ApiClient();
  late Future<List<CachedFiscalPeriod>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reloadLocal();
  }

  void _reloadLocal() {
    _future = widget.localDatabase.getCachedFiscalPeriods(
      companyId: widget.companyId,
      fiscalYearId: widget.fiscalYear.id,
    );
  }

  Future<void> _refreshOnline() async {
    if (_busy) return;
    setState(() => _busy = true);

    try {
      final periods = await _apiClient.getFiscalPeriods(
        bearerToken: widget.accessToken,
        fiscalYearId: widget.fiscalYear.id,
      );

      await widget.localDatabase.replaceFiscalPeriods(
        companyId: widget.companyId,
        fiscalYearId: widget.fiscalYear.id,
        periods: periods,
      );

      if (!mounted) return;
      setState(_reloadLocal);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _ensureStandard() async {
    if (_busy) return;
    setState(() => _busy = true);

    try {
      final periods =
          await _apiClient.ensureStandardFiscalPeriods(
        bearerToken: widget.accessToken,
        fiscalYearId: widget.fiscalYear.id,
      );

      await widget.localDatabase.replaceFiscalPeriods(
        companyId: widget.companyId,
        fiscalYearId: widget.fiscalYear.id,
        periods: periods,
      );

      if (!mounted) return;
      setState(_reloadLocal);

      _message('۱۲ دوره استاندارد سال مالی آماده شد.');
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _setClosed(
    CachedFiscalPeriod period,
    bool isClosed,
  ) async {
    if (_busy) return;
    setState(() => _busy = true);

    try {
      await _apiClient.setFiscalPeriodClosed(
        bearerToken: widget.accessToken,
        periodId: period.id,
        isClosed: isClosed,
      );

      final periods = await _apiClient.getFiscalPeriods(
        bearerToken: widget.accessToken,
        fiscalYearId: widget.fiscalYear.id,
      );

      await widget.localDatabase.replaceFiscalPeriods(
        companyId: widget.companyId,
        fiscalYearId: widget.fiscalYear.id,
        periods: periods,
      );

      if (!mounted) return;
      setState(_reloadLocal);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(text)),
    );
  }

  String _date(String value) {
    return JalaliDate.fromGregorian(
      DateTime.parse(value),
    ).formatted;
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: Text('دوره‌های ' + widget.fiscalYear.name),
          actions: [
            IconButton(
              tooltip: 'دریافت از سرور',
              onPressed: _busy ? null : _refreshOnline,
              icon: const Icon(Icons.sync),
            ),
          ],
        ),
        body: FutureBuilder<List<CachedFiscalPeriod>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(
                child: Text(snapshot.error.toString()),
              );
            }

            final periods =
                snapshot.data ?? const <CachedFiscalPeriod>[];

            if (periods.isEmpty) {
              return Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(
                        Icons.date_range_outlined,
                        size: 48,
                      ),
                      const SizedBox(height: 12),
                      const Text(
                        'برای این سال مالی هنوز دوره‌ای تعریف نشده است.',
                      ),
                      const SizedBox(height: 16),
                      FilledButton.icon(
                        onPressed: _busy ? null : _ensureStandard,
                        icon: const Icon(Icons.auto_fix_high_outlined),
                        label: const Text(
                          'ساخت ۱۲ دوره استاندارد شمسی',
                        ),
                      ),
                    ],
                  ),
                ),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: periods.length,
              separatorBuilder: (_, __) => const SizedBox(height: 10),
              itemBuilder: (context, index) {
                final period = periods[index];

                return Card(
                  child: ListTile(
                    leading: CircleAvatar(
                      child: Text(
                        period.periodNumber
                            .toString()
                            .padLeft(2, '0'),
                      ),
                    ),
                    title: Text(period.name),
                    subtitle: Text(
                      _date(period.startDate) +
                          ' تا ' +
                          _date(period.endDate),
                    ),
                    trailing: Wrap(
                      spacing: 8,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        Chip(
                          avatar: Icon(
                            period.isClosed
                                ? Icons.lock_outline
                                : Icons.lock_open_outlined,
                            size: 16,
                          ),
                          label: Text(
                            period.isClosed ? 'بسته' : 'باز',
                          ),
                        ),
                        PopupMenuButton<String>(
                          enabled: !_busy,
                          onSelected: (value) {
                            if (value == 'toggle') {
                              _setClosed(
                                period,
                                !period.isClosed,
                              );
                            }
                          },
                          itemBuilder: (_) => [
                            PopupMenuItem(
                              value: 'toggle',
                              child: Text(
                                period.isClosed
                                    ? 'بازگشایی دوره'
                                    : 'بستن دوره',
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                );
              },
            );
          },
        ),
      ),
    );
  }
}
