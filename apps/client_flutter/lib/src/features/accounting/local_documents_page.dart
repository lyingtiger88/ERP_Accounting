import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/demo/demo_mode.dart';
import '../../core/sync/accounting_sync_service.dart';
import 'audit_trail_page.dart';
import 'new_journal_page.dart';

class LocalDocumentsPage extends StatefulWidget {
  const LocalDocumentsPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<LocalDocumentsPage> createState() => _LocalDocumentsPageState();
}

class _LocalDocumentsPageState extends State<LocalDocumentsPage> {
  final _apiClient = ApiClient();
  late Future<List<LocalAccountingDocument>> _future;
  bool _syncing = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = widget.localDatabase.getLocalDocuments(widget.companyId);
  }

  Future<void> _syncPending() async {
    if (_syncing) return;

    setState(() => _syncing = true);

    try {
      final result = await AccountingSyncService(
        localDatabase: widget.localDatabase,
        apiClient: _apiClient,
      ).syncAll(
        companyId: widget.companyId,
        bearerToken: widget.accessToken,
      );

      if (!mounted) return;

      setState(_reload);

      final message = result.stoppedByNetwork
          ? 'اتصال قطع شد. Push: ' +
              result.pushed.toString() +
              '، باقی‌مانده: ' +
              result.remainingOutbox.toString()
          : 'Push: ' +
              result.pushed.toString() +
              ' • Pull: ' +
              result.pulled.toString() +
              ' • خطای Push: ' +
              result.pushFailed.toString() +
              ' • تعارض: ' +
              result.conflicts.toString() +
              ' • باقی‌مانده: ' +
              result.remainingOutbox.toString();

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(message)),
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('همگام‌سازی ناموفق بود: ' + error.toString()),
        ),
      );
    } finally {
      if (mounted) {
        setState(() => _syncing = false);
      }
    }
  }

  Future<void> _deleteDraft(
    LocalAccountingDocument document,
  ) async {
    if (document.status != 'Draft' ||
        document.syncStatus != 'LocalOnly' ||
        document.serverId != null) {
      return;
    }

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('حذف پیش‌نویس'),
        content: const Text(
          'این پیش‌نویس هنوز وارد صف همگام‌سازی نشده است. حذف آن دائمی است.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('حذف پیش‌نویس'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    try {
      await widget.localDatabase.deleteLocalJournalDraft(
        documentId: document.id,
        companyId: widget.companyId,
      );

      if (!mounted) return;
      setState(_reload);

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('پیش‌نویس حذف شد.'),
        ),
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(error.toString())),
      );
    }
  }

  Future<void> _editDraft(
    LocalAccountingDocument document,
  ) async {
    if (document.status != 'Draft' ||
        document.syncStatus != 'LocalOnly' ||
        document.serverId != null) {
      return;
    }

    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => NewJournalPage(
          companyId: widget.companyId,
          localDatabase: widget.localDatabase,
          draft: document,
          isDemoMode: DemoMode.isDemoToken(widget.accessToken),
        ),
      ),
    );

    if (!mounted) return;
    setState(_reload);
  }

  Future<void> _reverseJournal(
    LocalAccountingDocument document,
  ) async {
    final serverId = document.serverId;

    if (serverId == null ||
        document.reversalOfServerId != null ||
        document.reversedByServerId != null) {
      return;
    }

    final reasonController = TextEditingController();
    var reversalDate = DateTime.now();

    final draft = await showDialog<_ReversalDraft>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: Text(
                'برگشت سند ' +
                    (document.serverNumber ?? ''),
              ),
              content: SizedBox(
                width: 460,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    TextField(
                      controller: reasonController,
                      maxLines: 3,
                      decoration: const InputDecoration(
                        labelText: 'علت برگشت',
                        hintText:
                            'علت اصلاح/برگشت سند را ثبت کنید',
                      ),
                    ),
                    const SizedBox(height: 14),
                    OutlinedButton.icon(
                      onPressed: () async {
                        final picked = await showDatePicker(
                          context: context,
                          initialDate: reversalDate,
                          firstDate: DateTime(2000),
                          lastDate: DateTime(2100),
                        );

                        if (picked != null) {
                          setDialogState(
                            () => reversalDate = picked,
                          );
                        }
                      },
                      icon: const Icon(
                        Icons.calendar_month_outlined,
                      ),
                      label: Text(
                        reversalDate.year.toString() +
                            '/' +
                            reversalDate.month
                                .toString()
                                .padLeft(2, '0') +
                            '/' +
                            reversalDate.day
                                .toString()
                                .padLeft(2, '0'),
                      ),
                    ),
                    const SizedBox(height: 10),
                    const Text(
                      'سند اصلی دست‌نخورده می‌ماند و یک سند معکوس جدید با شماره رسمی ساخته می‌شود.',
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
                    final reason = reasonController.text.trim();
                    if (reason.isEmpty) return;

                    Navigator.pop(
                      context,
                      _ReversalDraft(
                        date: reversalDate,
                        reason: reason,
                      ),
                    );
                  },
                  child: const Text('ثبت برگشت'),
                ),
              ],
            );
          },
        );
      },
    );

    reasonController.dispose();
    if (draft == null) return;

    setState(() => _syncing = true);

    try {
      final response = await _apiClient.reverseJournal(
        bearerToken: widget.accessToken,
        journalEntryId: serverId,
        documentDate: draft.date,
        reason: draft.reason,
      );

      await AccountingSyncService(
        localDatabase: widget.localDatabase,
        apiClient: _apiClient,
      ).syncAll(
        companyId: widget.companyId,
        bearerToken: widget.accessToken,
      );

      if (!mounted) return;
      setState(_reload);

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'سند معکوس با شماره ' +
                response['reversalNumber'].toString() +
                ' ثبت شد.',
          ),
        ),
      );
    } on ApiException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(error.message)),
      );
    } finally {
      if (mounted) setState(() => _syncing = false);
    }
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
            if (!DemoMode.isDemoToken(widget.accessToken))
            IconButton(
              tooltip: 'همگام‌سازی دوطرفه',
              onPressed: _syncing ? null : _syncPending,
              icon: _syncing
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.sync),
            ),
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
                final synced = document.syncStatus == 'Synced';

                return Card(
                  child: ExpansionTile(
                    leading: CircleAvatar(
                      child: Icon(
                        synced
                            ? Icons.cloud_done_outlined
                            : pending
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
                          ' ردیف' +
                          (document.serverNumber == null
                              ? ''
                              : '  •  شماره قطعی: ' +
                                  document.serverNumber!) +
                          (document.reversalOfServerId == null
                              ? ''
                              : '  •  سند معکوس') +
                          (document.reversedByServerId == null
                              ? ''
                              : '  •  برگشت‌شده'),
                    ),
                    trailing: Chip(
                      avatar: Icon(
                        synced
                            ? Icons.cloud_done_outlined
                            : pending
                                ? Icons.schedule_send_outlined
                                : Icons.save_outlined,
                        size: 16,
                      ),
                      label: Text(
                        synced
                            ? 'Synced'
                            : pending
                                ? 'Pending Sync'
                                : 'Draft',
                      ),
                    ),
                    children: [
                      if (document.status == 'Draft' &&
                          document.syncStatus == 'LocalOnly' &&
                          document.serverId == null)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(20, 0, 20, 8),
                          child: Wrap(
                            spacing: 8,
                            runSpacing: 8,
                            children: [
                              FilledButton.tonalIcon(
                                onPressed: _syncing
                                    ? null
                                    : () => _editDraft(document),
                                icon: const Icon(Icons.edit_outlined),
                                label: const Text('ویرایش پیش‌نویس'),
                              ),
                              TextButton.icon(
                                onPressed: _syncing
                                    ? null
                                    : () => _deleteDraft(document),
                                icon: const Icon(Icons.delete_outline),
                                label: const Text('حذف پیش‌نویس'),
                              ),
                            ],
                          ),
                        ),
                      if (document.syncError != null)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(20, 0, 20, 12),
                          child: ListTile(
                            contentPadding: EdgeInsets.zero,
                            leading: Icon(
                              Icons.error_outline,
                              color: Theme.of(context).colorScheme.error,
                            ),
                            title: Text(
                              'خطای همگام‌سازی — تلاش ' +
                                  document.syncAttempts.toString(),
                            ),
                            subtitle: Text(document.syncError!),
                          ),
                        ),
                      if (document.reversalOfServerId != null)
                        const Padding(
                          padding: EdgeInsets.fromLTRB(20, 0, 20, 12),
                          child: ListTile(
                            contentPadding: EdgeInsets.zero,
                            leading: Icon(Icons.undo_outlined),
                            title: Text(
                              'این رکورد یک سند معکوس (Reversal) است.',
                            ),
                          ),
                        ),
                      if (document.reversedByServerId != null)
                        const Padding(
                          padding: EdgeInsets.fromLTRB(20, 0, 20, 12),
                          child: ListTile(
                            contentPadding: EdgeInsets.zero,
                            leading: Icon(Icons.history_outlined),
                            title: Text(
                              'برای این سند قبلاً سند برگشت ثبت شده است.',
                            ),
                          ),
                        ),
                      if (synced && document.serverId != null)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(20, 0, 20, 8),
                          child: Align(
                            alignment: Alignment.centerRight,
                            child: TextButton.icon(
                              onPressed: () {
                                Navigator.of(context).push(
                                  MaterialPageRoute<void>(
                                    builder: (_) => AuditTrailPage(
                                      accessToken: widget.accessToken,
                                      entityId: document.serverId,
                                      title: 'تاریخچه سند ' +
                                          (document.serverNumber ?? ''),
                                    ),
                                  ),
                                );
                              },
                              icon: const Icon(
                                Icons.manage_history_outlined,
                              ),
                              label: const Text('تاریخچه حسابرسی'),
                            ),
                          ),
                        ),
                      if (synced &&
                          document.serverId != null &&
                          document.reversalOfServerId == null &&
                          document.reversedByServerId == null)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(20, 0, 20, 12),
                          child: Align(
                            alignment: Alignment.centerRight,
                            child: OutlinedButton.icon(
                              onPressed: _syncing
                                  ? null
                                  : () => _reverseJournal(document),
                              icon: const Icon(Icons.undo_outlined),
                              label: const Text('برگشت سند'),
                            ),
                          ),
                        ),
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


class _ReversalDraft {
  const _ReversalDraft({
    required this.date,
    required this.reason,
  });

  final DateTime date;
  final String reason;
}
