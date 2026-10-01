import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';

class WarehousesPage extends StatefulWidget {
  const WarehousesPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<WarehousesPage> createState() => _WarehousesPageState();
}

class _WarehousesPageState extends State<WarehousesPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _loadWarehouses();
  }

  Future<List<Map<String, dynamic>>> _loadWarehouses() async {
    try {
      final items = await _apiClient.getWarehouses(
        bearerToken: widget.accessToken,
      );

      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: 'Warehouse',
        items: items,
      );

      return items;
    } on ApiException catch (error) {
      if (error.statusCode != null) rethrow;

      return widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'Warehouse',
      );
    }
  }

  Future<void> _edit([Map<String, dynamic>? existing]) async {
    final code = TextEditingController(
      text: existing?['code']?.toString() ?? '',
    );
    final name = TextEditingController(
      text: existing?['name']?.toString() ?? '',
    );
    var isActive = existing?['isActive'] as bool? ?? true;

    final saved = await showDialog<bool>(
      context: context,
      builder: (context) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: Text(
                existing == null
                    ? 'انبار جدید'
                    : 'ویرایش انبار',
              ),
              content: SizedBox(
                width: 420,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    TextField(
                      controller: code,
                      textDirection: TextDirection.ltr,
                      decoration: const InputDecoration(
                        labelText: 'کد انبار',
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: name,
                      decoration: const InputDecoration(
                        labelText: 'نام انبار',
                      ),
                    ),
                    if (existing != null) ...[
                      const SizedBox(height: 8),
                      SwitchListTile(
                        contentPadding: EdgeInsets.zero,
                        title: const Text('فعال'),
                        value: isActive,
                        onChanged: (value) {
                          setDialogState(
                            () => isActive = value,
                          );
                        },
                      ),
                    ],
                  ],
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () =>
                      Navigator.pop(context, false),
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
                  child: const Text('ذخیره'),
                ),
              ],
            );
          },
        );
      },
    );

    if (saved != true) {
      code.dispose();
      name.dispose();
      return;
    }

    setState(() => _busy = true);

    try {
      if (existing == null) {
        await _apiClient.createWarehouse(
          bearerToken: widget.accessToken,
          code: code.text.trim(),
          name: name.text.trim(),
        );
      } else {
        await _apiClient.updateWarehouse(
          bearerToken: widget.accessToken,
          warehouseId: existing['id'].toString(),
          code: code.text.trim(),
          name: name.text.trim(),
          isActive: isActive,
        );
      }

      if (!mounted) return;
      setState(_reload);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      code.dispose();
      name.dispose();

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
          title: const Text('انبارها'),
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _busy ? null : _edit,
          icon: const Icon(Icons.add_business_outlined),
          label: const Text('انبار جدید'),
        ),
        body: FutureBuilder<List<Map<String, dynamic>>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(
                child: CircularProgressIndicator(),
              );
            }

            if (snapshot.hasError) {
              return Center(
                child: Text(snapshot.error.toString()),
              );
            }

            final items =
                snapshot.data ?? const <Map<String, dynamic>>[];

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: items.length,
              separatorBuilder: (_, __) =>
                  const SizedBox(height: 10),
              itemBuilder: (context, index) {
                final item = items[index];

                return Card(
                  child: ListTile(
                    onTap: _busy
                        ? null
                        : () => _edit(item),
                    leading: const CircleAvatar(
                      child: Icon(Icons.warehouse_outlined),
                    ),
                    title: Text(
                      item['code'].toString() +
                          ' — ' +
                          item['name'].toString(),
                    ),
                    trailing: Wrap(
                      spacing: 8,
                      crossAxisAlignment:
                          WrapCrossAlignment.center,
                      children: [
                        Chip(
                          label: Text(
                            (item['isActive'] as bool? ?? true)
                                ? 'فعال'
                                : 'غیرفعال',
                          ),
                        ),
                        const Icon(Icons.edit_outlined),
                      ],
                    ),
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
