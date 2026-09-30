import 'package:flutter/material.dart';

import '../../core/database/local_database.dart';

class LocalDocumentsPage extends StatefulWidget {
  const LocalDocumentsPage({
    super.key,
    required this.companyId,
    required this.localDatabase,
  });

  final String companyId;
  final LocalDatabase localDatabase;

  @override
  State<LocalDocumentsPage> createState() => _LocalDocumentsPageState();
}

class _LocalDocumentsPageState extends State<LocalDocumentsPage> {
  late Future<List<LocalAccountingDocument>> _future;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = widget.localDatabase.getLocalDocuments(widget.companyId);
  }

  String _money(int value) {
    final raw = value.abs().toString();
    final buffer = StringBuffer();
    for (var i = 0; i < raw.length; i++) {
      if (i > 0 && (raw.length - i) % 3 == 0) {
        buffer.write(',');
      }
      buffer.write(raw[i]);
    }
    return (value < 0 ? '-' : '') + buffer.toString();
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('اسناد حسابداری محلی'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: FutureBuilder<List<LocalAccountingDocument>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(
                child: Text(
                  'خطا در خواندن اسناد: ' + snapshot.error.toString(),
                ),
              );
            }

            final documents =
                snapshot.data ?? const <LocalAccountingDocument>[];

            if (documents.isEmpty) {
              return const Center(
                child: Text('هنوز سند محلی ثبت نشده است.'),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: documents.length,
              separatorBuilder: (_, __) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final document = documents[index];
                final pending = document.syncStatus == 'Pending';

                return Card(
                  child: ExpansionTile(
                    leading: CircleAvatar(
                      child: Icon(
                        pending
                            ? Icons.cloud_upload_outlined
                            : Icons.edit_note_outlined,
                      ),
                    ),
                    title: Text(
                      document.description.isEmpty
                          ? 'سند بدون شرح'
                          : document.description,
                    ),
                    subtitle: Text(
                      document.documentDate +
                          '  •  ' +
                          document.status +
                          '  •  ' +
                          document.lineCount.toString() +
                          ' ردیف',
                    ),
                    trailing: Chip(
                      avatar: Icon(
                        pending
                            ? Icons.schedule_send_outlined
                            : Icons.save_outlined,
                        size: 16,
                      ),
                      label: Text(
                        pending ? 'Pending Sync' : 'Draft',
                      ),
                    ),
                    children: [
                      Padding(
                        padding: const EdgeInsets.fromLTRB(20, 0, 20, 12),
                        child: Row(
                          children: [
                            Expanded(
                              child: Text(
                                'بدهکار: ' +
                                    _money(document.debitTotal) +
                                    ' ریال',
                              ),
                            ),
                            Expanded(
                              child: Text(
                                'بستانکار: ' +
                                    _money(document.creditTotal) +
                                    ' ریال',
                              ),
                            ),
                            Icon(
                              document.isBalanced
                                  ? Icons.check_circle_outline
                                  : Icons.warning_amber_outlined,
                            ),
                          ],
                        ),
                      ),
                      FutureBuilder<List<LocalJournalLine>>(
                        future: widget.localDatabase.getLocalDocumentLines(
                          document.id,
                        ),
                        builder: (context, linesSnapshot) {
                          final lines =
                              linesSnapshot.data ?? const <LocalJournalLine>[];

                          if (linesSnapshot.connectionState !=
                              ConnectionState.done) {
                            return const Padding(
                              padding: EdgeInsets.all(16),
                              child: LinearProgressIndicator(),
                            );
                          }

                          return Column(
                            children: [
                              for (final line in lines)
                                ListTile(
                                  dense: true,
                                  title: Text(
                                    line.accountCode +
                                        ' — ' +
                                        line.accountName,
                                  ),
                                  subtitle: Text(
                                    [
                                      if (line.detailName != null)
                                        'تفصیلی: ' +
                                            (line.detailCode ?? '') +
                                            ' — ' +
                                            line.detailName!,
                                      if (line.description.isNotEmpty)
                                        line.description,
                                    ].join(' • '),
                                  ),
                                  trailing: Text(
                                    line.debit > 0
                                        ? _money(line.debit) + ' بدهکار'
                                        : _money(line.credit) + ' بستانکار',
                                  ),
                                ),
                            ],
                          );
                        },
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
