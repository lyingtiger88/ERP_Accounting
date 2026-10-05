import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/demo/demo_mode.dart';
import '../../core/demo/local_demo_business_engine.dart';
import '../accounting/report_support.dart';
import 'new_purchase_receipt_page.dart';

class PurchaseReceiptsPage extends StatefulWidget {
  const PurchaseReceiptsPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<PurchaseReceiptsPage> createState() =>
      _PurchaseReceiptsPageState();
}

class _PurchaseReceiptsPageState
    extends State<PurchaseReceiptsPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _loadReceipts();
  }

  Future<List<Map<String, dynamic>>> _loadReceipts() async {
    List<Map<String, dynamic>> serverItems;

    try {
      serverItems = await _apiClient.getPurchaseReceipts(
        bearerToken: widget.accessToken,
      );

      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'PurchaseReceipt',
        items: serverItems,
      );
    } on ApiException catch (error) {
      if (error.statusCode != null) rethrow;

      serverItems =
          await widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'PurchaseReceipt',
      );
    }

    final drafts = await widget.localDatabase.getLocalStoreDrafts(
      companyId: widget.companyId,
      entityType: 'StorePurchaseReceiptDraft',
    );
    final products =
        await widget.localDatabase.getCachedStoreEntities(
      companyId: widget.companyId,
      entityType: 'StoreProduct',
    );
    final warehouses =
        await widget.localDatabase.getCachedStoreEntities(
      companyId: widget.companyId,
      entityType: 'Warehouse',
    );

    final productMap = {
      for (final item in products)
        item['id'].toString(): item,
    };
    final warehouseMap = {
      for (final item in warehouses)
        item['id'].toString(): item,
    };

    final localItems = drafts
        .where((draft) => draft.syncStatus != 'Synced')
        .map((draft) {
          final payload = draft.payload;
          final lines =
              (payload['lines'] as List<dynamic>? ?? const [])
                  .map((raw) {
            final line = Map<String, dynamic>.from(raw as Map);
            final product =
                productMap[line['productId']?.toString()];
            final quantity = reportNumber(line['quantity']);
            final unitCost = reportNumber(
              line['unitCost'] ??
                  product?['defaultPurchasePrice'],
            );
            final discount =
                reportNumber(line['discountAmount']);

            return <String, dynamic>{
              ...line,
              'id': draft.id + ':' +
                  line['productId'].toString(),
              'sku': product?['sku']?.toString() ?? '?',
              'productName':
                  product?['name']?.toString() ?? 'کالای محلی',
              'unitName':
                  product?['unitName']?.toString() ?? 'عدد',
              'unitCost': unitCost,
              'netAmount': quantity * unitCost - discount,
            };
          }).toList(growable: false);

          final subtotal = lines.fold<num>(
            0,
            (sum, line) =>
                sum +
                reportNumber(line['quantity']) *
                    reportNumber(line['unitCost']),
          );
          final discount = lines.fold<num>(
            0,
            (sum, line) =>
                sum + reportNumber(line['discountAmount']),
          );
          final tax = lines.fold<num>(
            0,
            (sum, line) =>
                sum + reportNumber(line['taxAmount']),
          );

          final warehouse =
              warehouseMap[payload['warehouseId']?.toString()];

          return <String, dynamic>{
            'id': draft.id,
            'number': 'LOCAL-' +
                draft.id.substring(0, 8).toUpperCase(),
            'documentDate': payload['documentDate'],
            'warehouseName':
                warehouse?['name']?.toString() ?? 'انبار محلی',
            'supplierName': null,
            'currencyCode':
                payload['currencyCode']?.toString() ?? 'BASE',
            'exchangeRate': payload['exchangeRate'] ?? 1,
            'status': DemoMode.isDemoToken(widget.accessToken)
                ? 'Draft'
                : 'LocalPending',
            'subtotal': subtotal,
            'discountTotal': discount,
            'taxTotal': tax,
            'grandTotal': subtotal - discount + tax,
            'accountingJournalNumber': null,
            'syncError': draft.lastError,
            'lines': lines,
          };
        })
        .toList(growable: false);

    return [...localItems, ...serverItems];
  }

  Future<void> _create() async {
    final result = await Navigator.of(context).push<dynamic>(
      MaterialPageRoute<dynamic>(
        builder: (_) => NewPurchaseReceiptPage(
          companyId: widget.companyId,
          accessToken: widget.accessToken,
          localDatabase: widget.localDatabase,
        ),
      ),
    );

    if (result != null && mounted) {
      setState(_reload);
    }
  }

  Future<void> _post(Map<String, dynamic> receipt) async {
    if (_busy || receipt['status'].toString() != 'Draft') return;

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(
          'ثبت قطعی خرید ' + receipt['number'].toString(),
        ),
        content: const Text(
          'ثبت قطعی، موجودی را افزایش می‌دهد و سند حسابداری خرید/مالیات/پرداختنی یا صندوق را ایجاد می‌کند.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('ثبت قطعی'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    setState(() => _busy = true);

    try {
      final response = DemoMode.isDemoToken(widget.accessToken)
          ? await LocalDemoBusinessEngine(
              localDatabase: widget.localDatabase,
            ).postPurchaseReceipt(receipt['id'].toString())
          : await _apiClient.postPurchaseReceipt(
              bearerToken: widget.accessToken,
              receiptId: receipt['id'].toString(),
            );

      if (!mounted) return;
      setState(_reload);
      _message(
        'خرید ثبت شد • سند حسابداری: ' +
            response['accountingJournalNumber'].toString(),
      );
    } on ApiException catch (error) {
      _message(error.message);
    } on StateError catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String value) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(value)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('خرید و رسید انبار'),
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
          icon: const Icon(Icons.add_business_outlined),
          label: const Text('خرید جدید'),
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
                child: Text('هنوز رسید خریدی ثبت نشده است.'),
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
                          item['warehouseName'].toString() +
                          (item['supplierName'] == null
                              ? ''
                              : ' • ' +
                                  item['supplierName'].toString()),
                    ),
                    trailing: Chip(
                      label: Text(
                        item['status'].toString() == 'Posted'
                            ? 'قطعی'
                            : item['status'].toString() ==
                                    'LocalPending'
                                ? 'منتظر Sync'
                                : 'پیش‌نویس',
                      ),
                    ),
                    children: [
                      if (item['syncError'] != null)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(
                            20,
                            0,
                            20,
                            10,
                          ),
                          child: Align(
                            alignment: Alignment.centerRight,
                            child: Text(
                              'خطای Sync: ' +
                                  item['syncError'].toString(),
                            ),
                          ),
                        ),
                      for (final raw in lines)
                        Builder(
                          builder: (context) {
                            final line =
                                Map<String, dynamic>.from(raw as Map);
                            final trace = [
                              if (line['lotNumber'] != null)
                                'لات ' + line['lotNumber'].toString(),
                              if (line['serialNumber'] != null)
                                'سریال ' +
                                    line['serialNumber'].toString(),
                            ].join(' • ');

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
                                    (trace.isEmpty ? '' : ' • ' + trace),
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
                            if (item['status'].toString() == 'Draft')
                              FilledButton.icon(
                                onPressed: _busy
                                    ? null
                                    : () => _post(item),
                                icon: const Icon(
                                  Icons.check_circle_outline,
                                ),
                                label: const Text('ثبت قطعی'),
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
