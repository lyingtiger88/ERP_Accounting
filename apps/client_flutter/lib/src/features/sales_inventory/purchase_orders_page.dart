import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/demo/demo_mode.dart';
import '../../core/demo/local_demo_business_engine.dart';
import '../accounting/report_support.dart';
import 'new_purchase_order_page.dart';
import 'new_purchase_receipt_page.dart';

class PurchaseOrdersPage extends StatefulWidget {
  const PurchaseOrdersPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<PurchaseOrdersPage> createState() => _PurchaseOrdersPageState();
}

class _PurchaseOrdersPageState extends State<PurchaseOrdersPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _loadOrders();
  }

  Future<List<Map<String, dynamic>>> _loadOrders() async {
    List<Map<String, dynamic>> serverItems;

    try {
      serverItems = await _apiClient.getPurchaseOrders(
        bearerToken: widget.accessToken,
      );
      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'PurchaseOrder',
        items: serverItems,
      );
    } on ApiException catch (error) {
      if (error.statusCode != null) rethrow;
      serverItems = await widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'PurchaseOrder',
      );
    }

    final drafts = await widget.localDatabase.getLocalStoreDrafts(
      companyId: widget.companyId,
      entityType: 'StorePurchaseOrderDraft',
    );
    final products = await widget.localDatabase.getCachedStoreEntities(
      companyId: widget.companyId,
      entityType: 'StoreProduct',
    );
    final warehouses = await widget.localDatabase.getCachedStoreEntities(
      companyId: widget.companyId,
      entityType: 'Warehouse',
    );
    final details = await widget.localDatabase.getCachedDetailAccounts(
      widget.companyId,
    );

    final productMap = {
      for (final item in products) item['id'].toString(): item,
    };
    final warehouseMap = {
      for (final item in warehouses) item['id'].toString(): item,
    };
    final detailMap = {
      for (final item in details) item.id: item,
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
            final discount = reportNumber(line['discountAmount']);
            return <String, dynamic>{
              ...line,
              'id': draft.id + ':' + line['productId'].toString(),
              'sku': product?['sku']?.toString() ?? '?',
              'productName':
                  product?['name']?.toString() ?? 'کالای محلی',
              'unitName':
                  product?['unitName']?.toString() ?? 'عدد',
              'unitCost': unitCost,
              'receivedQuantity': 0,
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
          final supplier =
              detailMap[payload['supplierDetailAccountId']?.toString()];

          return <String, dynamic>{
            'id': draft.id,
            'number': 'LOCAL-' +
                draft.id.substring(0, 8).toUpperCase(),
            'documentDate': payload['documentDate'],
            'expectedDate': payload['expectedDate'],
            'warehouseName':
                warehouse?['name']?.toString() ?? 'انبار محلی',
            'supplierName':
                supplier?.name ?? 'تامین‌کننده محلی',
            'currencyCode':
                payload['currencyCode']?.toString() ?? 'BASE',
            'exchangeRate': payload['exchangeRate'] ?? 1,
            'status': DemoMode.isDemoToken(widget.accessToken)
                ? 'Draft'
                : 'LocalPending',
            'description': payload['description'],
            'subtotal': subtotal,
            'discountTotal': discount,
            'taxTotal': tax,
            'grandTotal': subtotal - discount + tax,
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
        builder: (_) => NewPurchaseOrderPage(
          companyId: widget.companyId,
          accessToken: widget.accessToken,
          localDatabase: widget.localDatabase,
        ),
      ),
    );

    if (result != null && mounted) {
      setState(_reload);
      _message('سفارش خرید ذخیره شد.');
    }
  }

  Future<void> _receive(
    Map<String, dynamic> order,
  ) async {
    final result = await Navigator.of(context).push<dynamic>(
      MaterialPageRoute<dynamic>(
        builder: (_) => NewPurchaseReceiptPage(
          companyId: widget.companyId,
          accessToken: widget.accessToken,
          localDatabase: widget.localDatabase,
          purchaseOrder: order,
        ),
      ),
    );

    if (result != null && mounted) {
      setState(_reload);
      _message(
        'رسید سفارش ' +
            order['number'].toString() +
            ' ذخیره شد.',
      );
    }
  }

  Future<void> _setStatus(
    Map<String, dynamic> order,
    String status,
  ) async {
    if (_busy) return;

    final labels = {
      'Approved': 'تایید',
      'Closed': 'بستن',
      'Cancelled': 'لغو',
    };
    final action = labels[status] ?? status;

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(
          action + ' سفارش ' + order['number'].toString(),
        ),
        content: Text(
          'آیا از ' + action + ' این سفارش خرید مطمئن هستید؟',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text(action),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    setState(() => _busy = true);
    try {
      if (DemoMode.isDemoToken(widget.accessToken)) {
        await LocalDemoBusinessEngine(
          localDatabase: widget.localDatabase,
        ).setPurchaseOrderStatus(
          orderId: order['id'].toString(),
          status: status,
        );
      } else {
        await _apiClient.setPurchaseOrderStatus(
          bearerToken: widget.accessToken,
          orderId: order['id'].toString(),
          status: status,
        );
      }

      if (!mounted) return;
      setState(_reload);
      _message('وضعیت سفارش خرید به‌روزرسانی شد.');
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

  String _statusTitle(String value) {
    switch (value) {
      case 'Approved':
        return 'تایید شده';
      case 'Closed':
        return 'بسته';
      case 'Cancelled':
        return 'لغو شده';
      case 'LocalPending':
        return 'منتظر Sync';
      default:
        return 'پیش‌نویس';
    }
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('سفارش‌های خرید'),
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
          icon: const Icon(Icons.add_shopping_cart_outlined),
          label: const Text('سفارش جدید'),
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
                child: Text('هنوز سفارش خریدی ثبت نشده است.'),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: items.length,
              separatorBuilder: (_, _) =>
                  const SizedBox(height: 10),
              itemBuilder: (context, index) {
                final item = items[index];
                final status = item['status'].toString();
                final lines =
                    item['lines'] as List<dynamic>? ?? const [];

                return Card(
                  child: ExpansionTile(
                    leading: const CircleAvatar(
                      child: Icon(Icons.shopping_cart_outlined),
                    ),
                    title: Text(
                      item['number'].toString() +
                          ' • ' +
                          formatReportMoney(
                            reportNumber(item['grandTotal']),
                          ) +
                          ' ' +
                          (item['currencyCode']?.toString() ??
                              'BASE'),
                    ),
                    subtitle: Text(
                      formatReportDate(
                            DateTime.parse(
                              item['documentDate'].toString(),
                            ),
                          ) +
                          ' • ' +
                          item['supplierName'].toString() +
                          ' • ' +
                          item['warehouseName'].toString(),
                    ),
                    trailing: Chip(
                      label: Text(_statusTitle(status)),
                    ),
                    children: [
                      if (item['description'] != null)
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
                              item['description'].toString(),
                            ),
                          ),
                        ),
                      for (final raw in lines)
                        Builder(
                          builder: (context) {
                            final line =
                                Map<String, dynamic>.from(
                              raw as Map,
                            );
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
                                    ' • دریافت‌شده: ' +
                                    formatReportMoney(
                                      reportNumber(
                                        line['receivedQuantity'],
                                      ),
                                    ),
                              ),
                              trailing: Text(
                                formatReportMoney(
                                  reportNumber(line['netAmount']) +
                                      reportNumber(
                                        line['taxAmount'],
                                      ),
                                ),
                              ),
                            );
                          },
                        ),
                      Padding(
                        padding: const EdgeInsets.fromLTRB(
                          20,
                          8,
                          20,
                          16,
                        ),
                        child: Wrap(
                          spacing: 10,
                          runSpacing: 8,
                          children: [
                            if (status == 'Draft') ...[
                              FilledButton.icon(
                                onPressed: _busy
                                    ? null
                                    : () => _setStatus(
                                          item,
                                          'Approved',
                                        ),
                                icon: const Icon(
                                  Icons.verified_outlined,
                                ),
                                label: const Text('تایید سفارش'),
                              ),
                              OutlinedButton.icon(
                                onPressed: _busy
                                    ? null
                                    : () => _setStatus(
                                          item,
                                          'Cancelled',
                                        ),
                                icon: const Icon(
                                  Icons.cancel_outlined,
                                ),
                                label: const Text('لغو'),
                              ),
                            ],
                            if (status == 'Approved') ...[
                              FilledButton.icon(
                                onPressed: _busy
                                    ? null
                                    : () => _receive(item),
                                icon: const Icon(
                                  Icons.inventory_2_outlined,
                                ),
                                label: const Text('دریافت کالا'),
                              ),
                              OutlinedButton.icon(
                                onPressed: _busy
                                    ? null
                                    : () => _setStatus(
                                          item,
                                          'Closed',
                                        ),
                                icon: const Icon(
                                  Icons.inventory_outlined,
                                ),
                                label: const Text('بستن دستی'),
                              ),
                              OutlinedButton.icon(
                                onPressed: _busy
                                    ? null
                                    : () => _setStatus(
                                          item,
                                          'Cancelled',
                                        ),
                                icon: const Icon(
                                  Icons.cancel_outlined,
                                ),
                                label: const Text('لغو'),
                              ),
                            ],
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
