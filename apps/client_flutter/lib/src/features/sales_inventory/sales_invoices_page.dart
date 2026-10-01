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
    _future = _apiClient.getSalesInvoices(
      bearerToken: widget.accessToken,
    );
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
  });

  final Map<String, dynamic> invoice;
  final bool busy;
  final String Function(String) statusText;
  final String Function(String) paymentText;
  final VoidCallback onPost;

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
              ' ریال',
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
