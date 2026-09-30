import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';

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

  Future<void> _createDetail() async {
    final code = TextEditingController();
    final name = TextEditingController();
    final nationalId = TextEditingController();
    var type = 'Customer';

    final accepted = await showDialog<bool>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: const Text('تفصیلی شناور جدید'),
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
                  ],
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(context, false),
                  child: const Text('انصراف'),
                ),
                FilledButton(
                  onPressed: () {
                    if (code.text.trim().isEmpty ||
                        name.text.trim().isEmpty) {
                      return;
                    }
                    Navigator.pop(context, true);
                  },
                  child: const Text('ایجاد'),
                ),
              ],
            );
          },
        );
      },
    );

    if (accepted != true) {
      code.dispose();
      name.dispose();
      nationalId.dispose();
      return;
    }

    setState(() => _busy = true);

    try {
      await _apiClient.createDetailAccount(
        bearerToken: widget.accessToken,
        code: code.text.trim(),
        name: name.text.trim(),
        type: type,
        nationalId: nationalId.text.trim().isEmpty
            ? null
            : nationalId.text.trim(),
      );

      await _refreshOnline();
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      code.dispose();
      name.dispose();
      nationalId.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(text)),
    );
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
              tooltip: 'دریافت از سرور',
              onPressed: _busy ? null : _refreshOnline,
              icon: const Icon(Icons.sync),
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
                  leading: const CircleAvatar(
                    child: Icon(Icons.person_outline),
                  ),
                  title: Text(item.code + ' — ' + item.name),
                  subtitle: Text(
                    _types[item.type] ?? item.type,
                  ),
                  trailing: item.nationalId == null
                      ? null
                      : Text(
                          item.nationalId!,
                          textDirection: TextDirection.ltr,
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
