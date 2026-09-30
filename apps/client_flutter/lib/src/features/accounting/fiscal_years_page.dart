import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/date/jalali_date.dart';

class FiscalYearsPage extends StatefulWidget {
  const FiscalYearsPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<FiscalYearsPage> createState() => _FiscalYearsPageState();
}

class _FiscalYearsPageState extends State<FiscalYearsPage> {
  final _apiClient = ApiClient();
  late Future<List<CachedFiscalYear>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reloadLocal();
  }

  void _reloadLocal() {
    _future = widget.localDatabase.getCachedFiscalYears(widget.companyId);
  }

  Future<void> _refreshOnline() async {
    setState(() => _busy = true);

    try {
      final items = await _apiClient.getFiscalYears(
        bearerToken: widget.accessToken,
      );

      await widget.localDatabase.replaceFiscalYears(
        companyId: widget.companyId,
        fiscalYears: items,
      );

      if (!mounted) return;
      setState(_reloadLocal);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _createYear() async {
    final currentPersianYear =
        JalaliDate.fromGregorian(DateTime.now()).year;
    final controller = TextEditingController(
      text: currentPersianYear.toString(),
    );
    var makeDefault = true;

    final result = await showDialog<(int, bool)>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: const Text('ایجاد سال مالی شمسی'),
              content: SizedBox(
                width: 380,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    TextField(
                      controller: controller,
                      keyboardType: TextInputType.number,
                      textDirection: TextDirection.ltr,
                      decoration: const InputDecoration(
                        labelText: 'سال شمسی',
                        hintText: '1405',
                      ),
                    ),
                    const SizedBox(height: 16),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('سال مالی پیش‌فرض'),
                      value: makeDefault,
                      onChanged: (value) {
                        setDialogState(() => makeDefault = value);
                      },
                    ),
                    const SizedBox(height: 8),
                    const Text(
                      'بازه به‌صورت خودکار از اول فروردین تا پایان اسفند ساخته می‌شود.',
                      style: TextStyle(fontSize: 12),
                    ),
                  ],
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(context),
                  child: const Text('انصراف'),
                ),
                FilledButton(
                  onPressed: () {
                    final year = int.tryParse(controller.text.trim());
                    if (year == null) return;
                    Navigator.pop(context, (year, makeDefault));
                  },
                  child: const Text('ایجاد'),
                ),
              ],
            );
          },
        );
      },
    );

    controller.dispose();
    if (result == null) return;

    final year = result.$1;
    final start = JalaliDate.toGregorian(year, 1, 1);
    final nextStart = JalaliDate.toGregorian(year + 1, 1, 1);
    final end = nextStart.subtract(const Duration(days: 1));

    setState(() => _busy = true);

    try {
      await _apiClient.createFiscalYear(
        bearerToken: widget.accessToken,
        name: 'سال مالی ' + year.toString(),
        persianYear: year,
        startDate: start,
        endDate: end,
        isDefault: result.$2,
      );

      await _refreshOnline();
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

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('سال‌های مالی'),
          actions: [
            IconButton(
              tooltip: 'دریافت از سرور',
              onPressed: _busy ? null : _refreshOnline,
              icon: const Icon(Icons.sync),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _busy ? null : _createYear,
          icon: const Icon(Icons.add),
          label: const Text('سال مالی جدید'),
        ),
        body: FutureBuilder<List<CachedFiscalYear>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(child: Text(snapshot.error.toString()));
            }

            final items = snapshot.data ?? const <CachedFiscalYear>[];

            if (items.isEmpty) {
              return const Center(
                child: Text('سال مالی در Cache محلی وجود ندارد.'),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: items.length,
              separatorBuilder: (_, __) => const SizedBox(height: 10),
              itemBuilder: (context, index) {
                final item = items[index];

                return Card(
                  child: ListTile(
                    leading: CircleAvatar(
                      child: Text(item.persianYear.toString().substring(2)),
                    ),
                    title: Text(item.name),
                    subtitle: Text(
                      'از ' +
                          JalaliDate.fromGregorian(
                            DateTime.parse(item.startDate),
                          ).formatted +
                          ' تا ' +
                          JalaliDate.fromGregorian(
                            DateTime.parse(item.endDate),
                          ).formatted,
                    ),
                    trailing: Wrap(
                      spacing: 6,
                      children: [
                        if (item.isDefault)
                          const Chip(label: Text('پیش‌فرض')),
                        Chip(
                          label: Text(item.isClosed ? 'بسته' : 'باز'),
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
