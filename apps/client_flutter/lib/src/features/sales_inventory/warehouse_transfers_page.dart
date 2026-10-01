import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../accounting/report_support.dart';
import 'new_warehouse_transfer_page.dart';

class WarehouseTransfersPage extends StatefulWidget {
  const WarehouseTransfersPage({
    super.key,
    required this.accessToken,
  });

  final String accessToken;

  @override
  State<WarehouseTransfersPage> createState() =>
      _WarehouseTransfersPageState();
}

class _WarehouseTransfersPageState
    extends State<WarehouseTransfersPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _apiClient.getWarehouseTransfers(
      bearerToken: widget.accessToken,
    );
  }

  Future<void> _create() async {
    final result = await Navigator.of(context).push<dynamic>(
      MaterialPageRoute<dynamic>(
        builder: (_) => NewWarehouseTransferPage(
          accessToken: widget.accessToken,
        ),
      ),
    );

    if (result != null && mounted) {
      setState(_reload);
    }
  }

  Future<void> _post(Map<String, dynamic> transfer) async {
    setState(() => _busy = true);

    try {
      await _apiClient.postWarehouseTransfer(
        bearerToken: widget.accessToken,
        transferId: transfer['id'].toString(),
      );

      if (mounted) setState(_reload);
    } on ApiException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(error.message)),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('انتقال بین انبارها'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _busy ? null : () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _busy ? null : _create,
          icon: const Icon(Icons.swap_horiz),
          label: const Text('انتقال جدید'),
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
                child: Text('هنوز انتقالی ثبت نشده است.'),
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
                    title: Text(item['number'].toString()),
                    subtitle: Text(
                      formatReportDate(
                            DateTime.parse(
                              item['documentDate'].toString(),
                            ),
                          ) +
                          ' • ' +
                          item['fromWarehouseName'].toString() +
                          ' ← ' +
                          item['toWarehouseName'].toString(),
                    ),
                    trailing: Chip(
                      label: Text(
                        item['status'].toString() == 'Posted'
                            ? 'قطعی'
                            : 'پیش‌نویس',
                      ),
                    ),
                    children: [
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
                                'تعداد: ' +
                                    formatReportMoney(
                                      reportNumber(line['quantity']),
                                    ) +
                                    (line['lotNumber'] == null
                                        ? ''
                                        : ' • لات ' +
                                            line['lotNumber'].toString()) +
                                    (line['serialNumber'] == null
                                        ? ''
                                        : ' • سریال ' +
                                            line['serialNumber'].toString()),
                              ),
                              trailing: Text(
                                formatReportMoney(
                                      reportNumber(line['unitCost']),
                                    ) +
                                    ' ریال',
                              ),
                            );
                          },
                        ),
                      if (item['status'].toString() == 'Draft')
                        Padding(
                          padding:
                              const EdgeInsets.fromLTRB(20, 8, 20, 16),
                          child: Align(
                            alignment: Alignment.centerRight,
                            child: FilledButton.icon(
                              onPressed: _busy
                                  ? null
                                  : () => _post(item),
                              icon: const Icon(
                                Icons.check_circle_outline,
                              ),
                              label: const Text('ثبت قطعی انتقال'),
                            ),
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
