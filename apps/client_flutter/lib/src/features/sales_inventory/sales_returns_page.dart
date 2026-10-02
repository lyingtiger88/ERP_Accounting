import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../accounting/report_support.dart';

class SalesReturnsPage extends StatefulWidget {
  const SalesReturnsPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<SalesReturnsPage> createState() =>
      _SalesReturnsPageState();
}

class _SalesReturnsPageState extends State<SalesReturnsPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _loadReturns();
  }

  Future<List<Map<String, dynamic>>> _loadReturns() async {
    try {
      final items = await _apiClient.getSalesReturns(
        bearerToken: widget.accessToken,
      );

      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'SalesReturn',
        items: items,
      );

      return items;
    } on ApiException catch (error) {
      if (error.statusCode != null) rethrow;

      return widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'SalesReturn',
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('برگشت از فروش'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: FutureBuilder<List<Map<String, dynamic>>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(child: Text(snapshot.error.toString()));
            }

            final items =
                snapshot.data ?? const <Map<String, dynamic>>[];

            if (items.isEmpty) {
              return const Center(
                child: Text('هنوز برگشت از فروشی ثبت نشده است.'),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: items.length,
              separatorBuilder: (_, __) => const SizedBox(height: 10),
              itemBuilder: (context, index) {
                final item = items[index];
                final lines =
                    item['lines'] as List<dynamic>? ?? const [];

                return Card(
                  child: ExpansionTile(
                    leading: const CircleAvatar(
                      child: Icon(Icons.assignment_return_outlined),
                    ),
                    title: Text(
                      item['number'].toString() +
                          ' • ' +
                          formatReportMoney(
                            reportNumber(item['grandTotal']),
                          ) +
                          ' ' +
                          (item['currencyCode']?.toString() ?? 'BASE'),
                    ),
                    subtitle: Text(
                      formatReportDate(
                            DateTime.parse(
                              item['documentDate'].toString(),
                            ),
                          ) +
                          ' • ' +
                          item['warehouseName'].toString(),
                    ),
                    trailing: const Chip(
                      label: Text('قطعی'),
                    ),
                    children: [
                      if (item['reason'] != null)
                        Padding(
                          padding:
                              const EdgeInsets.fromLTRB(20, 0, 20, 10),
                          child: Align(
                            alignment: Alignment.centerRight,
                            child: Text(
                              'علت: ' + item['reason'].toString(),
                            ),
                          ),
                        ),
                      for (final raw in lines)
                        Builder(
                          builder: (context) {
                            final line =
                                Map<String, dynamic>.from(raw as Map);

                            return ListTile(
                              dense: true,
                              title: Text(
                                line['sku'].toString() +
                                    ' — ' +
                                    line['productName'].toString(),
                              ),
                              subtitle: Text(
                                'تعداد برگشتی: ' +
                                    formatReportMoney(
                                      reportNumber(line['quantity']),
                                    ),
                              ),
                              trailing: Text(
                                formatReportMoney(
                                      reportNumber(line['netAmount']) +
                                          reportNumber(line['taxAmount']),
                                    ) +
                                    ' ' +
                                    (item['currencyCode']?.toString() ?? 'BASE'),
                              ),
                            );
                          },
                        ),
                      Padding(
                        padding:
                            const EdgeInsets.fromLTRB(20, 8, 20, 16),
                        child: Wrap(
                          spacing: 10,
                          children: [
                            Text(
                              'بهای برگشتی: ' +
                                  formatReportMoney(
                                    reportNumber(item['costTotal']),
                                  ) +
                                  ' ریال',
                            ),
                            if (item['accountingJournalNumber'] != null)
                              Chip(
                                avatar: const Icon(
                                  Icons.account_balance_outlined,
                                  size: 17,
                                ),
                                label: Text(
                                  'سند ' +
                                      item['accountingJournalNumber']
                                          .toString(),
                                ),
                              ),
                          ],
                        ),
                      ),
                    ],
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
