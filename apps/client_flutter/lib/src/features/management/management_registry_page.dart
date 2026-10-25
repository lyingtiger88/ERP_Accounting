import 'package:flutter/material.dart';

import '../../core/database/local_database.dart';

/// Local-only management register. It does not post journals or sync data.
class ManagementRegistryPage extends StatefulWidget {
  const ManagementRegistryPage({
    super.key,
    required this.companyId,
    required this.localDatabase,
  });

  final String companyId;
  final LocalDatabase localDatabase;

  @override
  State<ManagementRegistryPage> createState() => _ManagementRegistryPageState();
}

class _ManagementRegistryPageState extends State<ManagementRegistryPage> {
  static const _types = <String, String>{
    'LocalCrmContact': 'مخاطبان و CRM',
    'LocalFixedAsset': 'دارایی‌های ثابت',
    'LocalCheque': 'دفتر چک‌ها',
  };

  String _type = 'LocalCrmContact';
  List<Map<String, dynamic>> _items = [];
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  Future<void> _reload() async {
    final rows = await widget.localDatabase.getCachedStoreEntities(
      companyId: widget.companyId,
      entityType: _type,
    );
    if (mounted) setState(() => _items = rows);
  }

  Future<void> _edit([Map<String, dynamic>? existing]) async {
    final title = TextEditingController(text: existing?['title']?.toString() ?? '');
    final detail = TextEditingController(text: existing?['detail']?.toString() ?? '');
    final value = TextEditingController(text: existing?['value']?.toString() ?? '');
    final date = TextEditingController(text: existing?['date']?.toString() ?? '');
    final oldType = _type;
    final result = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(existing == null ? 'ثبت مورد جدید' : 'ویرایش اطلاعات'),
        content: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(controller: title, decoration: const InputDecoration(labelText: 'عنوان / نام / شماره چک')),
              const SizedBox(height: 12),
              TextField(controller: detail, decoration: const InputDecoration(labelText: 'توضیحات / طرف حساب')),
              const SizedBox(height: 12),
              TextField(controller: value, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'مبلغ / ارزش (ریال)')),
              const SizedBox(height: 12),
              TextField(controller: date, decoration: const InputDecoration(labelText: 'تاریخ (YYYY-MM-DD)')),
              const SizedBox(height: 8),
              const Text('این اطلاعات محلی هستند و سند حسابداری ایجاد نمی‌کنند.'),
            ],
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
          FilledButton(onPressed: () {
            if (title.text.trim().isEmpty) return;
            Navigator.pop(context, true);
          }, child: const Text('ذخیره')),
        ],
      ),
    );
    if (result != true || !mounted) {
      title.dispose(); detail.dispose(); value.dispose(); date.dispose();
      return;
    }
    setState(() => _busy = true);
    try {
      final updated = <String, dynamic>{
        'id': existing?['id']?.toString() ?? 'local-${DateTime.now().microsecondsSinceEpoch}',
        'title': title.text.trim(),
        'detail': detail.text.trim(),
        'value': value.text.trim(),
        'date': date.text.trim(),
        'createdAt': existing?['createdAt'] ?? DateTime.now().toUtc().toIso8601String(),
      };
      final current = (await widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId, entityType: oldType,
      )).toList();
      final index = current.indexWhere((e) => e['id'] == updated['id']);
      if (index >= 0) {
        current[index] = updated;
      } else {
        current.add(updated);
      }
      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId, entityType: oldType, items: current,
      );
      if (mounted && oldType == _type) await _reload();
    } finally {
      title.dispose(); detail.dispose(); value.dispose(); date.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _delete(Map<String, dynamic> item) async {
    final approved = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('حذف رکورد محلی'),
        content: const Text('این رکورد از دفتر محلی حذف می‌شود. ادامه می‌دهید؟'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('حذف')),
        ],
      ),
    );
    if (approved != true) return;
    setState(() => _busy = true);
    try {
      await widget.localDatabase.replaceStoreEntities(
        companyId: widget.companyId,
        entityType: _type,
        items: _items.where((entry) => entry['id'] != item['id']).toList(),
      );
      await _reload();
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Directionality(
    textDirection: TextDirection.rtl,
    child: Scaffold(
      appBar: AppBar(title: const Text('دفتر مدیریت زر')),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(children: [
              DropdownButtonFormField<String>(
                initialValue: _type,
                decoration: const InputDecoration(labelText: 'بخش'),
                items: _types.entries.map((e) => DropdownMenuItem(
                  value: e.key, child: Text(e.value),
                )).toList(),
                onChanged: _busy ? null : (value) {
                  if (value == null) return;
                  setState(() => _type = value);
                  _reload();
                },
              ),
              const SizedBox(height: 8),
              const Text('نسخه مقدماتی: داده‌ها فقط روی این دستگاه ذخیره می‌شوند و وارد حسابداری یا سرور نمی‌شوند.'),
              const SizedBox(height: 8),
              FilledButton.icon(
                onPressed: _busy ? null : () => _edit(),
                icon: const Icon(Icons.add),
                label: const Text('ثبت مورد جدید'),
              ),
            ]),
          ),
          Expanded(
            child: _items.isEmpty
                ? const Center(child: Text('موردی ثبت نشده است.'))
                : ListView.builder(
                    itemCount: _items.length,
                    itemBuilder: (context, i) {
                      final item = _items[i];
                      return ListTile(
                        title: Text(item['title']?.toString() ?? ''),
                        subtitle: Text('${item['detail'] ?? ''}  •  ${item['value'] ?? ''} ریال  •  ${item['date'] ?? ''}'),
                        trailing: Wrap(mainAxisSize: MainAxisSize.min, children: [
                          IconButton(onPressed: _busy ? null : () => _edit(item), icon: const Icon(Icons.edit_outlined)),
                          IconButton(onPressed: _busy ? null : () => _delete(item), icon: const Icon(Icons.delete_outline)),
                        ]),
                      );
                    },
                  ),
          ),
        ],
      ),
    ),
  );
}
