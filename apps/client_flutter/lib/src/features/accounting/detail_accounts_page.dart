import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/sync/accounting_sync_service.dart';

class DetailAccountsPage extends StatefulWidget {
  const DetailAccountsPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<DetailAccountsPage> createState() => _DetailAccountsPageState();
}

class _DetailAccountsPageState extends State<DetailAccountsPage> {
  static const _types = <String, String>{
    'Customer': 'مشتری',
    'Supplier': 'فروشنده / تأمین‌کننده',
    'Person': 'شخص',
    'Employee': 'کارمند',
    'Bank': 'بانک',
    'Government': 'سازمان دولتی',
    'Other': 'سایر',
  };

  final _apiClient = ApiClient();
  late Future<List<CachedDetailAccount>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reloadLocal();
  }

  void _reloadLocal() {
    _future =
        widget.localDatabase.getCachedDetailAccounts(widget.companyId);
  }

  Future<void> _refreshOnline() async {
    if (_busy) return;
    setState(() => _busy = true);

    try {
      final items = await _apiClient.getDetailAccounts(
        bearerToken: widget.accessToken,
      );

      await widget.localDatabase.replaceDetailAccounts(
        companyId: widget.companyId,
        details: items,
      );

      if (!mounted) return;
      setState(_reloadLocal);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _syncBestEffort() async {
    try {
      final result = await AccountingSyncService(
        localDatabase: widget.localDatabase,
        apiClient: _apiClient,
      ).syncAll(
        companyId: widget.companyId,
        bearerToken: widget.accessToken,
      );

      if (!mounted) return;

      if (result.conflicts > 0) {
        _message(
          result.conflicts.toString() +
              ' تعارض شناسایی شد و نیاز به تصمیم شما دارد.',
        );
      }
    } catch (_) {
      // Offline-first: local mutation remains safely queued.
    }

    if (mounted) {
      setState(_reloadLocal);
    }
  }

  Future<void> _createDetail() async {
    final draft = await _showDetailDialog(
      title: 'تفصیلی شناور جدید',
    );

    if (draft == null) return;

    setState(() => _busy = true);

    try {
      await widget.localDatabase.saveLocalDetailAccount(
        companyId: widget.companyId,
        code: draft.code,
        name: draft.name,
        type: draft.type,
        nationalId: draft.nationalId,
      );

      if (!mounted) return;
      setState(_reloadLocal);

      await _syncBestEffort();
    } on ArgumentError catch (error) {
      _message(error.message?.toString() ?? 'اطلاعات تفصیلی معتبر نیست.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _editDetail(CachedDetailAccount detail) async {
    if (detail.syncStatus == 'Conflict') {
      await _resolveConflict(detail);
      return;
    }

    final draft = await _showDetailDialog(
      title: 'ویرایش تفصیلی',
      initial: detail,
    );

    if (draft == null) return;

    setState(() => _busy = true);

    try {
      await widget.localDatabase.updateLocalDetailAccount(
        detail: detail,
        code: draft.code,
        name: draft.name,
        type: draft.type,
        nationalId: draft.nationalId,
        isActive: true,
      );

      if (!mounted) return;
      setState(_reloadLocal);

      await _syncBestEffort();
    } on ArgumentError catch (error) {
      _message(error.message?.toString() ?? 'اطلاعات تفصیلی معتبر نیست.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<_DetailDraft?> _showDetailDialog({
    required String title,
    CachedDetailAccount? initial,
  }) async {
    final code = TextEditingController(text: initial?.code ?? '');
    final name = TextEditingController(text: initial?.name ?? '');
    final nationalId = TextEditingController(
      text: initial?.nationalId ?? '',
    );
    var type = initial?.type ?? 'Customer';

    final result = await showDialog<_DetailDraft>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: Text(title),
              content: SizedBox(
                width: 430,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    TextField(
                      controller: code,
                      textDirection: TextDirection.ltr,
                      decoration: const InputDecoration(
                        labelText: 'کد تفصیلی',
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: name,
                      decoration: const InputDecoration(
                        labelText: 'نام',
                      ),
                    ),
                    const SizedBox(height: 12),
                    DropdownButtonFormField<String>(
                      initialValue: type,
                      decoration: const InputDecoration(
                        labelText: 'نوع تفصیلی',
                      ),
                      items: [
                        for (final entry in _types.entries)
                          DropdownMenuItem(
                            value: entry.key,
                            child: Text(entry.value),
                          ),
                      ],
                      onChanged: (value) {
                        if (value != null) {
                          setDialogState(() => type = value);
                        }
                      },
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: nationalId,
                      textDirection: TextDirection.ltr,
                      decoration: const InputDecoration(
                        labelText: 'شناسه / کد ملی (اختیاری)',
                      ),
                    ),
                    if (initial != null) ...[
                      const SizedBox(height: 14),
                      Align(
                        alignment: Alignment.centerRight,
                        child: Text(
                          'Revision فعلی: ' +
                              initial.revision.toString(),
                          style: const TextStyle(fontSize: 12),
                        ),
                      ),
                    ],
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
                    if (code.text.trim().isEmpty ||
                        name.text.trim().isEmpty) {
                      return;
                    }

                    Navigator.pop(
                      context,
                      _DetailDraft(
                        code: code.text.trim(),
                        name: name.text.trim(),
                        type: type,
                        nationalId: nationalId.text.trim().isEmpty
                            ? null
                            : nationalId.text.trim(),
                      ),
                    );
                  },
                  child: Text(initial == null ? 'ایجاد' : 'ذخیره'),
                ),
              ],
            );
          },
        );
      },
    );

    code.dispose();
    name.dispose();
    nationalId.dispose();

    return result;
  }

  Future<void> _resolveConflict(CachedDetailAccount detail) async {
    final conflict =
        await widget.localDatabase.getDetailAccountConflict(detail.id);

    if (conflict == null) {
      _message('جزئیات تعارض محلی پیدا نشد.');
      return;
    }

    final server = conflict.serverPayload;
    final local = conflict.localPayload;

    final choice = await showDialog<String>(
      context: context,
      builder: (context) {
        return AlertDialog(
          title: const Text('حل تعارض تفصیلی'),
          content: SizedBox(
            width: 560,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'Revision مبنا: ' +
                      conflict.baseRevision.toString() +
                      '   •   Revision سرور: ' +
                      (conflict.serverRevision?.toString() ?? 'ناموجود'),
                ),
                const SizedBox(height: 16),
                _ConflictVersionCard(
                  title: 'نسخه محلی',
                  code: local['code']?.toString() ?? '',
                  name: local['name']?.toString() ?? '',
                  type: _types[local['type']?.toString()] ??
                      local['type']?.toString() ??
                      '',
                  nationalId: local['nationalId']?.toString(),
                ),
                const SizedBox(height: 12),
                _ConflictVersionCard(
                  title: 'نسخه سرور',
                  code: server?['code']?.toString() ?? '—',
                  name: server?['name']?.toString() ?? '—',
                  type: server == null
                      ? '—'
                      : (_types[server['type']?.toString()] ??
                          server['type']?.toString() ??
                          ''),
                  nationalId: server?['nationalId']?.toString(),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('بعداً'),
            ),
            if (server != null)
              OutlinedButton(
                onPressed: () => Navigator.pop(context, 'server'),
                child: const Text('استفاده از نسخه سرور'),
              ),
            if (conflict.serverRevision != null)
              FilledButton(
                onPressed: () => Navigator.pop(context, 'local'),
                child: const Text('حفظ نسخه محلی و تلاش مجدد'),
              ),
          ],
        );
      },
    );

    if (choice == null) return;

    setState(() => _busy = true);

    try {
      if (choice == 'server') {
        await widget.localDatabase.resolveDetailConflictKeepServer(
          conflict,
        );
      } else if (choice == 'local') {
        await widget.localDatabase.resolveDetailConflictKeepLocal(
          conflict,
        );
        await _syncBestEffort();
      }

      if (mounted) setState(_reloadLocal);
    } catch (error) {
      _message(error.toString());
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

  String _statusLabel(CachedDetailAccount item) {
    switch (item.syncStatus) {
      case 'Pending':
        return 'Pending';
      case 'Conflict':
        return 'Conflict';
      default:
        return 'Synced';
    }
  }

  IconData _statusIcon(CachedDetailAccount item) {
    switch (item.syncStatus) {
      case 'Pending':
        return Icons.cloud_upload_outlined;
      case 'Conflict':
        return Icons.sync_problem_outlined;
      default:
        return Icons.cloud_done_outlined;
    }
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('تفصیلی‌های شناور'),
          actions: [
            IconButton(
              tooltip: 'همگام‌سازی',
              onPressed: _busy
                  ? null
                  : () async {
                      setState(() => _busy = true);
                      try {
                        await _syncBestEffort();
                      } finally {
                        if (mounted) setState(() => _busy = false);
                      }
                    },
              icon: _busy
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.sync),
            ),
            IconButton(
              tooltip: 'دریافت مستقیم از سرور',
              onPressed: _busy ? null : _refreshOnline,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _busy ? null : _createDetail,
          icon: const Icon(Icons.person_add_alt_outlined),
          label: const Text('تفصیلی جدید'),
        ),
        body: FutureBuilder<List<CachedDetailAccount>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(child: Text(snapshot.error.toString()));
            }

            final items =
                snapshot.data ?? const <CachedDetailAccount>[];

            if (items.isEmpty) {
              return const Center(
                child: Text('هنوز تفصیلی شناوری تعریف نشده است.'),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: items.length,
              separatorBuilder: (_, __) => const Divider(height: 1),
              itemBuilder: (context, index) {
                final item = items[index];

                return ListTile(
                  onTap: _busy ? null : () => _editDetail(item),
                  leading: CircleAvatar(
                    child: Icon(_statusIcon(item)),
                  ),
                  title: Text(item.code + ' — ' + item.name),
                  subtitle: Text(
                    (_types[item.type] ?? item.type) +
                        ' • Revision ' +
                        item.revision.toString() +
                        (item.syncError == null
                            ? ''
                            : ' • ' + item.syncError!),
                  ),
                  trailing: Wrap(
                    spacing: 8,
                    crossAxisAlignment: WrapCrossAlignment.center,
                    children: [
                      Chip(label: Text(_statusLabel(item))),
                      if (item.nationalId != null)
                        Text(
                          item.nationalId!,
                          textDirection: TextDirection.ltr,
                        ),
                      Icon(
                        item.syncStatus == 'Conflict'
                            ? Icons.rule_folder_outlined
                            : Icons.edit_outlined,
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

class _DetailDraft {
  const _DetailDraft({
    required this.code,
    required this.name,
    required this.type,
    required this.nationalId,
  });

  final String code;
  final String name;
  final String type;
  final String? nationalId;
}

class _ConflictVersionCard extends StatelessWidget {
  const _ConflictVersionCard({
    required this.title,
    required this.code,
    required this.name,
    required this.type,
    required this.nationalId,
  });

  final String title;
  final String code;
  final String name;
  final String type;
  final String? nationalId;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              title,
              style: const TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            Text('کد: ' + code),
            Text('نام: ' + name),
            Text('نوع: ' + type),
            Text('شناسه: ' + (nationalId ?? '—')),
          ],
        ),
      ),
    );
  }
}
