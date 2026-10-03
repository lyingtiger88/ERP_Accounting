import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';

class AccountingDimensionsPage extends StatefulWidget {
  const AccountingDimensionsPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<AccountingDimensionsPage> createState() =>
      _AccountingDimensionsPageState();
}

class _AccountingDimensionsPageState
    extends State<AccountingDimensionsPage> {
  final _apiClient = ApiClient();
  late Future<_DimensionData> _future;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _load();
  }

  Future<_DimensionData> _load() async {
    try {
      final costCenters = await _apiClient.getCostCenters(
        bearerToken: widget.accessToken,
      );
      final projects = await _apiClient.getAccountingProjects(
        bearerToken: widget.accessToken,
      );

      await widget.localDatabase.replaceCostCenters(
        companyId: widget.companyId,
        costCenters: costCenters,
      );
      await widget.localDatabase.replaceAccountingProjects(
        companyId: widget.companyId,
        projects: projects,
      );
    } on ApiException {
      // Cached master data remains usable when the server is unavailable.
    }

    final costCenters = await widget.localDatabase.getCachedCostCenters(
      widget.companyId,
      activeOnly: false,
    );
    final projects =
        await widget.localDatabase.getCachedAccountingProjects(
      widget.companyId,
      activeOnly: false,
    );

    return _DimensionData(
      costCenters: costCenters,
      projects: projects,
    );
  }

  Future<_DimensionDraft?> _showEditor({
    required String title,
    String code = '',
    String name = '',
    bool isActive = true,
    bool canChangeState = false,
  }) async {
    final codeController = TextEditingController(text: code);
    final nameController = TextEditingController(text: name);
    var active = isActive;

    final result = await showDialog<_DimensionDraft>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(title),
          content: SizedBox(
            width: 460,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextField(
                  controller: codeController,
                  enabled: !_busy,
                  decoration: const InputDecoration(
                    labelText: 'کد',
                  ),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: nameController,
                  enabled: !_busy,
                  decoration: const InputDecoration(
                    labelText: 'نام',
                  ),
                ),
                if (canChangeState) ...[
                  const SizedBox(height: 12),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    value: active,
                    title: const Text('فعال'),
                    onChanged: (value) {
                      setDialogState(() => active = value);
                    },
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
                final trimmedCode = codeController.text.trim();
                final trimmedName = nameController.text.trim();

                if (trimmedCode.isEmpty || trimmedName.isEmpty) {
                  return;
                }

                Navigator.pop(
                  context,
                  _DimensionDraft(
                    code: trimmedCode,
                    name: trimmedName,
                    isActive: active,
                  ),
                );
              },
              child: const Text('ذخیره'),
            ),
          ],
        ),
      ),
    );

    codeController.dispose();
    nameController.dispose();
    return result;
  }

  Future<void> _createCostCenter() async {
    final draft = await _showEditor(title: 'مرکز هزینه جدید');
    if (draft == null) return;

    await _runWrite(() async {
      await _apiClient.createCostCenter(
        bearerToken: widget.accessToken,
        code: draft.code,
        name: draft.name,
      );
    }, 'مرکز هزینه ثبت شد.');
  }

  Future<void> _editCostCenter(CachedCostCenter item) async {
    final draft = await _showEditor(
      title: 'ویرایش مرکز هزینه',
      code: item.code,
      name: item.name,
      isActive: item.isActive,
      canChangeState: true,
    );
    if (draft == null) return;

    await _runWrite(() async {
      await _apiClient.updateCostCenter(
        bearerToken: widget.accessToken,
        costCenterId: item.id,
        code: draft.code,
        name: draft.name,
        isActive: draft.isActive,
      );
    }, 'مرکز هزینه به‌روزرسانی شد.');
  }

  Future<void> _createProject() async {
    final draft = await _showEditor(title: 'پروژه جدید');
    if (draft == null) return;

    await _runWrite(() async {
      await _apiClient.createAccountingProject(
        bearerToken: widget.accessToken,
        code: draft.code,
        name: draft.name,
      );
    }, 'پروژه ثبت شد.');
  }

  Future<void> _editProject(CachedAccountingProject item) async {
    final draft = await _showEditor(
      title: 'ویرایش پروژه',
      code: item.code,
      name: item.name,
      isActive: item.isActive,
      canChangeState: true,
    );
    if (draft == null) return;

    await _runWrite(() async {
      await _apiClient.updateAccountingProject(
        bearerToken: widget.accessToken,
        projectId: item.id,
        code: draft.code,
        name: draft.name,
        isActive: draft.isActive,
      );
    }, 'پروژه به‌روزرسانی شد.');
  }

  Future<void> _runWrite(
    Future<void> Function() action,
    String successMessage,
  ) async {
    if (_busy) return;
    setState(() => _busy = true);

    try {
      await action();
      if (!mounted) return;
      setState(_reload);
      _message(successMessage);
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

  Widget _sectionHeader({
    required String title,
    required String subtitle,
    required VoidCallback onAdd,
  }) {
    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
              ),
              const SizedBox(height: 4),
              Text(subtitle),
            ],
          ),
        ),
        FilledButton.icon(
          onPressed: _busy ? null : onAdd,
          icon: const Icon(Icons.add),
          label: const Text('جدید'),
        ),
      ],
    );
  }

  Widget _empty(String text) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 22),
      child: Center(child: Text(text)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('ابعاد حسابداری'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _busy ? null : () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: FutureBuilder<_DimensionData>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(child: Text(snapshot.error.toString()));
            }

            final data = snapshot.data!;

            return ListView(
              padding: const EdgeInsets.all(24),
              children: [
                _sectionHeader(
                  title: 'مراکز هزینه',
                  subtitle: 'تفکیک هزینه و درآمد بر اساس واحد یا بخش',
                  onAdd: _createCostCenter,
                ),
                const SizedBox(height: 12),
                if (data.costCenters.isEmpty)
                  _empty('هنوز مرکز هزینه‌ای تعریف نشده است.')
                else
                  ...data.costCenters.map(
                    (item) => Card(
                      child: ListTile(
                        leading: const Icon(Icons.hub_outlined),
                        title: Text(item.code + ' — ' + item.name),
                        subtitle: Text(
                          item.isActive ? 'فعال' : 'غیرفعال',
                        ),
                        trailing: IconButton(
                          tooltip: 'ویرایش',
                          onPressed: _busy
                              ? null
                              : () => _editCostCenter(item),
                          icon: const Icon(Icons.edit_outlined),
                        ),
                      ),
                    ),
                  ),
                const SizedBox(height: 32),
                _sectionHeader(
                  title: 'پروژه‌ها',
                  subtitle: 'ردیابی مالی پروژه، قرارداد یا فعالیت',
                  onAdd: _createProject,
                ),
                const SizedBox(height: 12),
                if (data.projects.isEmpty)
                  _empty('هنوز پروژه حسابداری تعریف نشده است.')
                else
                  ...data.projects.map(
                    (item) => Card(
                      child: ListTile(
                        leading: const Icon(Icons.work_outline),
                        title: Text(item.code + ' — ' + item.name),
                        subtitle: Text(
                          item.isActive ? 'فعال' : 'غیرفعال',
                        ),
                        trailing: IconButton(
                          tooltip: 'ویرایش',
                          onPressed: _busy
                              ? null
                              : () => _editProject(item),
                          icon: const Icon(Icons.edit_outlined),
                        ),
                      ),
                    ),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _DimensionData {
  const _DimensionData({
    required this.costCenters,
    required this.projects,
  });

  final List<CachedCostCenter> costCenters;
  final List<CachedAccountingProject> projects;
}

class _DimensionDraft {
  const _DimensionDraft({
    required this.code,
    required this.name,
    required this.isActive,
  });

  final String code;
  final String name;
  final bool isActive;
}
