import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import 'report_support.dart';

class CurrencyPositionPage extends StatefulWidget {
  const CurrencyPositionPage({
    super.key,
    required this.accessToken,
  });

  final String accessToken;

  @override
  State<CurrencyPositionPage> createState() =>
      _CurrencyPositionPageState();
}

class _CurrencyPositionPageState
    extends State<CurrencyPositionPage> {
  final _apiClient = ApiClient();

  DateTime _asOf = DateTime.now();
  Map<String, dynamic>? _report;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final report = await _apiClient.getCurrencyPosition(
        bearerToken: widget.accessToken,
        asOf: _asOf,
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

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _asOf,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );

    if (picked == null || !mounted) return;

    setState(() => _asOf = picked);
    await _load();
  }

  @override
  Widget build(BuildContext context) {
    final report = _report;
    final baseCode =
        report?['baseCurrencyCode']?.toString() ?? '';
    final rows = report == null
        ? const <Map<String, dynamic>>[]
        : (report['rows'] as List<dynamic>)
            .map(
              (item) =>
                  Map<String, dynamic>.from(item as Map),
            )
            .toList(growable: false);

    final difference = rows.fold<num>(
      0,
      (sum, row) =>
          sum +
          reportNumber(row['unrealizedDifference']),
    );

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('موقعیت و تسعیر ارزی'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _loading ? null : _load,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Wrap(
                  spacing: 12,
                  runSpacing: 12,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    OutlinedButton.icon(
                      onPressed: _loading ? null : _pickDate,
                      icon: const Icon(
                        Icons.event_available_outlined,
                      ),
                      label: Text(
                        'تا تاریخ: ' + formatReportDate(_asOf),
                      ),
                    ),
                    if (baseCode.isNotEmpty)
                      Chip(
                        avatar: const Icon(
                          Icons.home_outlined,
                          size: 17,
                        ),
                        label: Text(
                          'ارز پایه: ' + baseCode,
                        ),
                      ),
                    if (!_loading && _error == null)
                      Chip(
                        avatar: const Icon(
                          Icons.calculate_outlined,
                          size: 17,
                        ),
                        label: Text(
                          'جمع تفاوت تسعیر: ' +
                              formatReportMoney(difference) +
                              (baseCode.isEmpty
                                  ? ''
                                  : ' ' + baseCode),
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
                  title: const Text(
                    'خطا در محاسبه موقعیت ارزی',
                  ),
                  subtitle: Text(_error!),
                ),
              )
            else if (_loading)
              const Padding(
                padding: EdgeInsets.all(40),
                child: Center(child: CircularProgressIndicator()),
              )
            else if (rows.isEmpty)
              const Padding(
                padding: EdgeInsets.all(40),
                child: Center(
                  child: Text(
                    'تا این تاریخ مانده ارزی ثبت‌شده‌ای در حساب‌های دارایی/بدهی وجود ندارد.',
                  ),
                ),
              )
            else
              Card(
                child: SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: DataTable(
                    columns: const [
                      DataColumn(label: Text('ارز')),
                      DataColumn(label: Text('حساب')),
                      DataColumn(
                        numeric: true,
                        label: Text('مانده ارزی'),
                      ),
                      DataColumn(
                        numeric: true,
                        label: Text('معادل تاریخی'),
                      ),
                      DataColumn(
                        numeric: true,
                        label: Text('نرخ روز'),
                      ),
                      DataColumn(
                        numeric: true,
                        label: Text('معادل تسعیرشده'),
                      ),
                      DataColumn(
                        numeric: true,
                        label: Text('تفاوت تسعیر'),
                      ),
                    ],
                    rows: [
                      for (final row in rows)
                        DataRow(
                          cells: [
                            DataCell(
                              Text(
                                row['currencyCode'].toString(),
                              ),
                            ),
                            DataCell(
                              Text(
                                row['accountCode'].toString() +
                                    ' — ' +
                                    row['accountName'].toString(),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    row['foreignBalance'],
                                  ),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    row['historicalBaseBalance'],
                                  ),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    row['currentRate'],
                                  ),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    row['revaluedBaseBalance'],
                                  ),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    row['unrealizedDifference'],
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
        ),
      ),
    );
  }
}
