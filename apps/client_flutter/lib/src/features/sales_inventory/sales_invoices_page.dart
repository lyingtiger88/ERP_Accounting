import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../accounting/report_support.dart';
import 'new_sales_invoice_page.dart';

class SalesInvoicesPage extends StatefulWidget {
  const SalesInvoicesPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<SalesInvoicesPage> createState() =>
      _SalesInvoicesPageState();
}

class _SalesInvoicesPageState
    extends State<SalesInvoicesPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _loadInvoices();
  }

  Future<List<Map<String, dynamic>>> _loadInvoices() async {
    List<Map<String, dynamic>> serverItems;

    try {
      serverItems = await _apiClient.getSalesInvoices(
        bearerToken: widget.accessToken,
      );

      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'SalesInvoice',
        items: serverItems,
      );
    } on ApiException catch (error) {
      if (error.statusCode != null) rethrow;

      serverItems =
          await widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'SalesInvoice',
      );
    }

    final drafts = await widget.localDatabase.getLocalStoreDrafts(
      companyId: widget.companyId,
      entityType: 'StoreSalesInvoiceDraft',
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
      for (final product in products)
        product['id'].toString(): product,
    };
    final warehouseMap = {
      for (final warehouse in warehouses)
        warehouse['id'].toString(): warehouse,
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

            return <String, dynamic>{
              ...line,
              'id': draft.id + ':' +
                  line['productId'].toString(),
              'sku': product?['sku']?.toString() ?? '?',
              'productName':
                  product?['name']?.toString() ?? 'کالای محلی',
              'unitName':
                  product?['unitName']?.toString() ?? 'عدد',
              'netAmount':
                  reportNumber(line['quantity']) *
                          reportNumber(line['unitPrice']) -
                      reportNumber(line['discountAmount']),
              'unitCost': 0,
              'costAmount': 0,
            };
          }).toList(growable: false);

          final subtotal = lines.fold<num>(
            0,
            (sum, line) =>
                sum +
                reportNumber(line['quantity']) *
                    reportNumber(line['unitPrice']),
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
            'paymentType': payload['paymentType'],
            'currencyCode':
                payload['currencyCode']?.toString() ?? 'BASE',
            'exchangeRate': payload['exchangeRate'] ?? 1,
            'customerName': null,
            'status': 'LocalPending',
            'description': payload['description'],
            'subtotal': subtotal,
            'discountTotal': discount,
            'taxTotal': tax,
            'grandTotal': subtotal - discount + tax,
            'costTotal': 0,
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
        builder: (_) => NewSalesInvoicePage(
          companyId: widget.companyId,
          accessToken: widget.accessToken,
          localDatabase: widget.localDatabase,
        ),
      ),
    );

    if (result == null || !mounted) return;

    setState(_reload);

    final number =
        (result as Map)['number']?.toString() ?? '';
    _message(
      number.isEmpty
          ? 'پیش‌نویس فاکتور ذخیره شد.'
          : 'پیش‌نویس فاکتور ' + number + ' ذخیره شد.',
    );
  }

  Future<void> _post(
    Map<String, dynamic> invoice,
  ) async {
    if (_busy || invoice['status'].toString() != 'Draft') {
      return;
    }

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) {
        return AlertDialog(
          title: Text(
            'ثبت قطعی فاکتور ' +
                invoice['number'].toString(),
          ),
          content: const SizedBox(
            width: 480,
            child: Text(
              'بعد از ثبت قطعی، موجودی کالا از انبار کم می‌شود، بهای تمام‌شده محاسبه می‌شود و سند حسابداری فروش به‌صورت خودکار ساخته خواهد شد. فاکتور قطعی دیگر Draft محسوب نمی‌شود.',
            ),
          ),
          actions: [
            TextButton(
              onPressed: () =>
                  Navigator.pop(context, false),
              child: const Text('انصراف'),
            ),
            FilledButton.icon(
              onPressed: () =>
                  Navigator.pop(context, true),
              icon: const Icon(
                Icons.check_circle_outline,
              ),
              label: const Text('ثبت قطعی'),
            ),
          ],
        );
      },
    );

    if (confirmed != true) return;

    setState(() => _busy = true);

    try {
      final response = await _apiClient.postSalesInvoice(
        bearerToken: widget.accessToken,
        invoiceId: invoice['id'].toString(),
      );

      if (!mounted) return;
      setState(_reload);

      _message(
        'فاکتور ثبت شد • سند حسابداری: ' +
            response['accountingJournalNumber'].toString(),
      );
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _returnInvoice(
    Map<String, dynamic> invoice,
  ) async {
    if (_busy || invoice['status'].toString() != 'Posted') {
      return;
    }

    final rawLines =
        invoice['lines'] as List<dynamic>? ?? const [];
    final lines = rawLines
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);

    final controllers = <String, TextEditingController>{
      for (final line in lines)
        line['id'].toString():
            TextEditingController(text: '0'),
    };
    final reason = TextEditingController();
    var date = DateTime.now();

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: Text(
                'برگشت از ' + invoice['number'].toString(),
              ),
              content: SizedBox(
                width: 620,
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      for (final line in lines)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: Row(
                            children: [
                              Expanded(
                                child: Text(
                                  line['sku'].toString() +
                                      ' — ' +
                                      line['productName'].toString() +
                                      ' • فروش: ' +
                                      formatReportMoney(
                                        reportNumber(
                                          line['quantity'],
                                        ),
                                      ),
                                ),
                              ),
                              SizedBox(
                                width: 130,
                                child: TextField(
                                  controller: controllers[
                                      line['id'].toString()],
                                  keyboardType:
                                      const TextInputType.numberWithOptions(
                                    decimal: true,
                                  ),
                                  textDirection: TextDirection.ltr,
                                  decoration: const InputDecoration(
                                    labelText: 'تعداد برگشت',
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                      TextField(
                        controller: reason,
                        maxLines: 2,
                        decoration: const InputDecoration(
                          labelText: 'علت برگشت',
                        ),
                      ),
                      const SizedBox(height: 12),
                      OutlinedButton.icon(
                        onPressed: () async {
                          final picked = await showDatePicker(
                            context: context,
                            initialDate: date,
                            firstDate: DateTime(2000),
                            lastDate: DateTime(2100),
                          );

                          if (picked != null) {
                            setDialogState(() => date = picked);
                          }
                        },
                        icon: const Icon(
                          Icons.calendar_month_outlined,
                        ),
                        label: Text(formatReportDate(date)),
                      ),
                    ],
                  ),
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () =>
                      Navigator.pop(context, false),
                  child: const Text('انصراف'),
                ),
                FilledButton.icon(
                  onPressed: () {
                    if (reason.text.trim().isEmpty) return;
                    final hasQuantity = controllers.values.any(
                      (controller) =>
                          (num.tryParse(
                                controller.text
                                    .replaceAll(',', '')
                                    .trim(),
                              ) ??
                              0) >
                          0,
                    );
                    if (!hasQuantity) return;

                    Navigator.pop(context, true);
                  },
                  icon: const Icon(
                    Icons.assignment_return_outlined,
                  ),
                  label: const Text('ثبت برگشت'),
                ),
              ],
            );
          },
        );
      },
    );

    if (confirmed != true) {
      for (final controller in controllers.values) {
        controller.dispose();
      }
      reason.dispose();
      return;
    }

    final requestLines = <Map<String, dynamic>>[];
    for (final line in lines) {
      final value = num.tryParse(
            controllers[line['id'].toString()]!
                .text
                .replaceAll(',', '')
                .trim(),
          ) ??
          0;

      if (value > 0) {
        requestLines.add({
          'salesInvoiceLineId': line['id'].toString(),
          'quantity': value,
        });
      }
    }

    setState(() => _busy = true);

    try {
      final result = await _apiClient.createSalesReturn(
        bearerToken: widget.accessToken,
        invoiceId: invoice['id'].toString(),
        documentDate: date,
        reason: reason.text.trim(),
        lines: requestLines,
      );

      if (!mounted) return;
      setState(_reload);

      _message(
        'برگشت ' +
            result['number'].toString() +
            ' ثبت شد • سند: ' +
            result['accountingJournalNumber'].toString(),
      );
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      for (final controller in controllers.values) {
        controller.dispose();
      }
      reason.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(text)),
    );
  }

  String _statusText(String status) {
    switch (status) {
      case 'Draft':
        return 'پیش‌نویس';
      case 'Posted':
        return 'قطعی';
      case 'Reversed':
        return 'برگشت‌شده';
      case 'LocalPending':
        return 'منتظر Sync';
      default:
        return status;
    }
  }

  String _paymentText(String paymentType) {
    return paymentType == 'Credit' ? 'نسیه' : 'نقدی';
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('فاکتورهای فروش'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed:
                  _busy ? null : () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _busy ? null : _create,
          icon: const Icon(Icons.add_shopping_cart_outlined),
          label: const Text('فاکتور فروش جدید'),
        ),
        body: FutureBuilder<List<Map<String, dynamic>>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState !=
                ConnectionState.done) {
              return const Center(
                child: CircularProgressIndicator(),
              );
            }

            if (snapshot.hasError) {
              return Center(
                child: Text(snapshot.error.toString()),
              );
            }

            final invoices =
                snapshot.data ??
                const <Map<String, dynamic>>[];

            if (invoices.isEmpty) {
              return const Center(
                child: Text(
                  'هنوز فاکتور فروشی ثبت نشده است.',
                ),
              );
            }

            final postedTotal = invoices
                .where(
                  (invoice) =>
                      invoice['status'].toString() ==
                      'Posted',
                )
                .fold<num>(
                  0,
                  (sum, invoice) =>
                      sum +
                      reportNumber(
                        invoice['grandTotal'],
                      ),
                );

            return ListView(
              padding: const EdgeInsets.all(20),
              children: [
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Wrap(
                      spacing: 24,
                      runSpacing: 10,
                      children: [
                        Text(
                          'تعداد فاکتورها: ' +
                              invoices.length.toString(),
                        ),
                        Text(
                          'فروش قطعی: ' +
                              formatReportMoney(
                                postedTotal,
                              ) +
                              ' ریال',
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 12),
                for (final invoice in invoices) ...[
                  _InvoiceCard(
                    invoice: invoice,
                    busy: _busy,
                    statusText: _statusText,
                    paymentText: _paymentText,
                    onPost: () => _post(invoice),
                    onReturn: () => _returnInvoice(invoice),
                  ),
                  const SizedBox(height: 10),
                ],
              ],
            );
          },
        ),
      ),
    );
  }
}

class _InvoiceCard extends StatelessWidget {
  const _InvoiceCard({
    required this.invoice,
    required this.busy,
    required this.statusText,
    required this.paymentText,
    required this.onPost,
    required this.onReturn,
  });

  final Map<String, dynamic> invoice;
  final bool busy;
  final String Function(String) statusText;
  final String Function(String) paymentText;
  final VoidCallback onPost;
  final VoidCallback onReturn;

  @override
  Widget build(BuildContext context) {
    final status = invoice['status'].toString();
    final posted = status == 'Posted';
    final lines =
        invoice['lines'] as List<dynamic>? ?? const [];

    return Card(
      child: ExpansionTile(
        leading: CircleAvatar(
          child: Icon(
            posted
                ? Icons.receipt_long
                : Icons.edit_note_outlined,
          ),
        ),
        title: Text(
          invoice['number'].toString() +
              ' • ' +
              formatReportMoney(
                reportNumber(invoice['grandTotal']),
              ) +
              ' ' +
              (invoice['currencyCode']?.toString() ?? 'BASE'),
        ),
        subtitle: Text(
          formatReportDate(
                DateTime.parse(
                  invoice['documentDate'].toString(),
                ),
              ) +
              ' • ' +
              invoice['warehouseName'].toString() +
              ' • ' +
              paymentText(
                invoice['paymentType'].toString(),
              ) +
              (invoice['customerName'] == null
                  ? ''
                  : ' • ' +
                      invoice['customerName'].toString()),
        ),
        trailing: Chip(
          label: Text(statusText(status)),
        ),
        children: [
          Padding(
            padding:
                const EdgeInsets.fromLTRB(20, 0, 20, 12),
            child: Wrap(
              spacing: 22,
              runSpacing: 8,
              children: [
                Text(
                  'ناخالص: ' +
                      formatReportMoney(
                        reportNumber(
                          invoice['subtotal'],
                        ),
                      ),
                ),
                Text(
                  'تخفیف: ' +
                      formatReportMoney(
                        reportNumber(
                          invoice['discountTotal'],
                        ),
                      ),
                ),
                Text(
                  'مالیات: ' +
                      formatReportMoney(
                        reportNumber(
                          invoice['taxTotal'],
                        ),
                      ),
                ),
                if (posted)
                  Text(
                    'بهای تمام‌شده: ' +
                        formatReportMoney(
                          reportNumber(
                            invoice['costTotal'],
                          ),
                        ),
                  ),
              ],
            ),
          ),
          if (invoice['syncError'] != null)
            Padding(
              padding:
                  const EdgeInsets.fromLTRB(20, 0, 20, 10),
              child: Align(
                alignment: Alignment.centerRight,
                child: Text(
                  'خطای Sync: ' +
                      invoice['syncError'].toString(),
                ),
              ),
            ),
          if (invoice['description'] != null)
            Padding(
              padding:
                  const EdgeInsets.fromLTRB(20, 0, 20, 12),
              child: Align(
                alignment: Alignment.centerRight,
                child: Text(
                  invoice['description'].toString(),
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
                    'تعداد: ' +
                        formatReportMoney(
                          reportNumber(line['quantity']),
                        ) +
                        ' ' +
                        line['unitName'].toString() +
                        ' • فی: ' +
                        formatReportMoney(
                          reportNumber(line['unitPrice']),
                        ),
                  ),
                  trailing: Text(
                    formatReportMoney(
                          reportNumber(line['netAmount']) +
                              reportNumber(
                                line['taxAmount'],
                              ),
                        ) +
                        ' ریال',
                  ),
                );
              },
            ),
          Padding(
            padding:
                const EdgeInsets.fromLTRB(20, 8, 20, 16),
            child: Wrap(
              spacing: 10,
              runSpacing: 8,
              children: [
                if (status == 'Draft')
                  FilledButton.icon(
                    onPressed: busy ? null : onPost,
                    icon: const Icon(
                      Icons.check_circle_outline,
                    ),
                    label: const Text('ثبت قطعی'),
                  ),
                if (status == 'Posted')
                  OutlinedButton.icon(
                    onPressed: busy ? null : onReturn,
                    icon: const Icon(
                      Icons.assignment_return_outlined,
                    ),
                    label: const Text('برگشت از فروش'),
                  ),
                if (invoice['accountingJournalNumber'] !=
                    null)
                  Chip(
                    avatar: const Icon(
                      Icons.account_balance_outlined,
                      size: 17,
                    ),
                    label: Text(
                      'سند حسابداری ' +
                          invoice['accountingJournalNumber']
                              .toString(),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
