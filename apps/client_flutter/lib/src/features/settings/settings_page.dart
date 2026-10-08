import 'dart:io';

import 'package:flutter/material.dart';
import 'package:path/path.dart' as p;

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/demo/demo_mode.dart';
import '../auth/login_page.dart';

class SettingsPage extends StatefulWidget {
  const SettingsPage({
    super.key,
    required this.role,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String role;
  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends State<SettingsPage> {
  static const _roles = <String>[
    'Owner',
    'Administrator',
    'FinancialManager',
    'Accountant',
    'InventoryManager',
    'Sales',
    'Viewer',
  ];

  final _api = ApiClient();

  bool _loadingUsers = false;
  bool _busy = false;
  List<Map<String, dynamic>> _users = const [];
  List<String> _backups = const [];

  bool get _isDemo => DemoMode.isDemoToken(widget.accessToken);

  bool get _canManageUsers =>
      !_isDemo &&
      (widget.role == 'Owner' || widget.role == 'Administrator');

  @override
  void initState() {
    super.initState();
    _loadAll();
  }

  Future<void> _loadAll() async {
    await Future.wait([
      _loadUsers(),
      _loadBackups(),
    ]);
  }

  Future<void> _loadUsers() async {
    if (!_canManageUsers) return;

    if (mounted) {
      setState(() => _loadingUsers = true);
    }

    try {
      final users = await _api.getAdminUsers(
        bearerToken: widget.accessToken,
      );

      if (!mounted) return;
      setState(() => _users = users);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) {
        setState(() => _loadingUsers = false);
      }
    }
  }

  Future<void> _loadBackups() async {
    final backups = await widget.localDatabase.listBackupFiles();
    if (!mounted) return;
    setState(() => _backups = backups);
  }

  Future<void> _createUser() async {
    final username = TextEditingController();
    final displayName = TextEditingController();
    final password = TextEditingController();
    var role = 'Viewer';
    var obscure = true;

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('کاربر جدید'),
          content: SizedBox(
            width: 560,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextField(
                    controller: username,
                    textDirection: TextDirection.ltr,
                    decoration: const InputDecoration(
                      labelText: 'نام کاربری',
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: displayName,
                    decoration: const InputDecoration(
                      labelText: 'نام نمایشی',
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: password,
                    obscureText: obscure,
                    textDirection: TextDirection.ltr,
                    decoration: InputDecoration(
                      labelText: 'رمز عبور',
                      helperText: 'حداقل ۱۰ کاراکتر',
                      suffixIcon: IconButton(
                        onPressed: () => setDialogState(
                          () => obscure = !obscure,
                        ),
                        icon: Icon(
                          obscure
                              ? Icons.visibility_outlined
                              : Icons.visibility_off_outlined,
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    initialValue: role,
                    isExpanded: true,
                    decoration: const InputDecoration(
                      labelText: 'نقش',
                    ),
                    items: [
                      for (final value in _roles)
                        DropdownMenuItem(
                          value: value,
                          child: Text(_roleTitle(value)),
                        ),
                    ],
                    onChanged: (value) {
                      if (value != null) {
                        setDialogState(() => role = value);
                      }
                    },
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('انصراف'),
            ),
            FilledButton(
              onPressed: () {
                if (username.text.trim().isEmpty ||
                    displayName.text.trim().isEmpty ||
                    password.text.length < 10) {
                  return;
                }
                Navigator.pop(context, true);
              },
              child: const Text('ایجاد کاربر'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true) {
      username.dispose();
      displayName.dispose();
      password.dispose();
      return;
    }

    setState(() => _busy = true);
    try {
      await _api.createAdminUser(
        bearerToken: widget.accessToken,
        username: username.text.trim(),
        displayName: displayName.text.trim(),
        password: password.text,
        role: role,
      );
      _message('کاربر ایجاد شد.');
      await _loadUsers();
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      username.dispose();
      displayName.dispose();
      password.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _editUser(Map<String, dynamic> user) async {
    final displayName = TextEditingController(
      text: user['displayName']?.toString() ?? '',
    );
    var role = user['role']?.toString() ?? 'Viewer';
    var isActive = user['isActive'] as bool? ?? true;

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(
            'ویرایش ' + user['username'].toString(),
          ),
          content: SizedBox(
            width: 520,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextField(
                  controller: displayName,
                  decoration: const InputDecoration(
                    labelText: 'نام نمایشی',
                  ),
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  initialValue: role,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'نقش',
                  ),
                  items: [
                    for (final value in _roles)
                      DropdownMenuItem(
                        value: value,
                        child: Text(_roleTitle(value)),
                      ),
                  ],
                  onChanged: (value) {
                    if (value != null) {
                      setDialogState(() => role = value);
                    }
                  },
                ),
                SwitchListTile(
                  value: isActive,
                  title: const Text('حساب فعال باشد'),
                  onChanged: (value) {
                    setDialogState(() => isActive = value);
                  },
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
                if (displayName.text.trim().isEmpty) return;
                Navigator.pop(context, true);
              },
              child: const Text('ذخیره'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true) {
      displayName.dispose();
      return;
    }

    setState(() => _busy = true);
    try {
      await _api.updateAdminUser(
        bearerToken: widget.accessToken,
        userId: user['id'].toString(),
        displayName: displayName.text.trim(),
        role: role,
        isActive: isActive,
      );
      _message('کاربر به‌روزرسانی شد.');
      await _loadUsers();
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      displayName.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _resetPassword(Map<String, dynamic> user) async {
    final controller = TextEditingController();
    var obscure = true;

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(
            'تغییر رمز ' + user['username'].toString(),
          ),
          content: TextField(
            controller: controller,
            obscureText: obscure,
            textDirection: TextDirection.ltr,
            decoration: InputDecoration(
              labelText: 'رمز جدید',
              helperText:
                  'حداقل ۱۰ کاراکتر؛ Sessionهای قبلی این کاربر باطل می‌شوند.',
              suffixIcon: IconButton(
                onPressed: () => setDialogState(
                  () => obscure = !obscure,
                ),
                icon: Icon(
                  obscure
                      ? Icons.visibility_outlined
                      : Icons.visibility_off_outlined,
                ),
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('انصراف'),
            ),
            FilledButton(
              onPressed: () {
                if (controller.text.length < 10) return;
                Navigator.pop(context, true);
              },
              child: const Text('تغییر رمز'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true) {
      controller.dispose();
      return;
    }

    setState(() => _busy = true);
    try {
      await _api.resetAdminUserPassword(
        bearerToken: widget.accessToken,
        userId: user['id'].toString(),
        newPassword: controller.text,
      );
      _message('رمز عبور تغییر کرد و Sessionهای قبلی باطل شدند.');
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      controller.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _createBackup() async {
    setState(() => _busy = true);
    try {
      final path = await widget.localDatabase.createBackup();
      await _loadBackups();
      _message('پشتیبان ساخته شد: ' + path);
    } catch (error) {
      _message('ساخت پشتیبان ناموفق بود: ' + error.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _restoreBackup(String path) async {
    final file = File(path);
    final stat = await file.stat();

    if (!mounted) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('بازیابی پشتیبان'),
        content: Text(
          'این کار دیتابیس محلی فعلی را با پشتیبان زیر جایگزین می‌کند:

' +
              p.basename(path) +
              '
' +
              _fileSize(stat.size) +
              '

قبل از جایگزینی، نسخه ایمنی خودکار ساخته می‌شود.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('بازیابی'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    setState(() => _busy = true);
    try {
      await widget.localDatabase.restoreBackup(path);
      if (!mounted) return;

      Navigator.of(context).pushAndRemoveUntil(
        MaterialPageRoute<void>(
          builder: (_) => LoginPage(
            localDatabase: widget.localDatabase,
          ),
        ),
        (route) => false,
      );
    } catch (error) {
      _message('بازیابی ناموفق بود: ' + error.toString());
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

  static String _roleTitle(String role) {
    switch (role) {
      case 'Owner':
        return 'مالک';
      case 'Administrator':
        return 'مدیر سیستم';
      case 'FinancialManager':
        return 'مدیر مالی';
      case 'Accountant':
        return 'حسابدار';
      case 'InventoryManager':
        return 'مدیر انبار';
      case 'Sales':
        return 'فروش';
      case 'Viewer':
        return 'فقط مشاهده';
      default:
        return role;
    }
  }

  static String _fileSize(int bytes) {
    if (bytes >= 1024 * 1024) {
      return (bytes / (1024 * 1024)).toStringAsFixed(1) + ' MB';
    }
    if (bytes >= 1024) {
      return (bytes / 1024).toStringAsFixed(1) + ' KB';
    }
    return bytes.toString() + ' B';
  }

  @override
  Widget build(BuildContext context) {
    final tabs = _canManageUsers ? 2 : 1;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('تنظیمات و ایمنی داده'),
        ),
        body: DefaultTabController(
          length: tabs,
          child: Column(
            children: [
              TabBar(
                tabs: [
                  if (_canManageUsers)
                    const Tab(
                      icon: Icon(Icons.manage_accounts_outlined),
                      text: 'کاربران و نقش‌ها',
                    ),
                  const Tab(
                    icon: Icon(Icons.backup_outlined),
                    text: 'پشتیبان محلی',
                  ),
                ],
              ),
              Expanded(
                child: TabBarView(
                  children: [
                    if (_canManageUsers) _usersTab(),
                    _backupTab(),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _usersTab() {
    if (_loadingUsers) {
      return const Center(child: CircularProgressIndicator());
    }

    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: Align(
            alignment: Alignment.centerRight,
            child: FilledButton.icon(
              onPressed: _busy ? null : _createUser,
              icon: const Icon(Icons.person_add_alt_1_outlined),
              label: const Text('کاربر جدید'),
            ),
          ),
        ),
        Expanded(
          child: _users.isEmpty
              ? const Center(
                  child: Text('کاربری برای نمایش وجود ندارد.'),
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
                  itemCount: _users.length,
                  separatorBuilder: (_, _) =>
                      const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final user = _users[index];
                    final active = user['isActive'] as bool? ?? true;

                    return Card(
                      child: ListTile(
                        leading: CircleAvatar(
                          child: Icon(
                            active
                                ? Icons.person_outline
                                : Icons.person_off_outlined,
                          ),
                        ),
                        title: Text(
                          user['username'].toString() +
                              ' — ' +
                              user['displayName'].toString(),
                        ),
                        subtitle: Text(
                          _roleTitle(user['role'].toString()) +
                              ' • ' +
                              (active ? 'فعال' : 'غیرفعال') +
                              ((user['mfaEnabled'] as bool? ?? false)
                                  ? ' • MFA'
                                  : ''),
                        ),
                        trailing: Wrap(
                          spacing: 4,
                          children: [
                            IconButton(
                              tooltip: 'ویرایش',
                              onPressed: _busy
                                  ? null
                                  : () => _editUser(user),
                              icon: const Icon(Icons.edit_outlined),
                            ),
                            IconButton(
                              tooltip: 'تغییر رمز',
                              onPressed: _busy
                                  ? null
                                  : () => _resetPassword(user),
                              icon: const Icon(Icons.password_outlined),
                            ),
                          ],
                        ),
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }

  Widget _backupTab() {
    return ListView(
      padding: const EdgeInsets.all(20),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Wrap(
              spacing: 12,
              runSpacing: 12,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                FilledButton.icon(
                  onPressed: _busy ? null : _createBackup,
                  icon: const Icon(Icons.backup_outlined),
                  label: const Text('ساخت پشتیبان'),
                ),
                OutlinedButton.icon(
                  onPressed: _busy ? null : _loadBackups,
                  icon: const Icon(Icons.refresh),
                  label: const Text('بازخوانی فهرست'),
                ),
                Text(
                  widget.localDatabase.databasePath ??
                      'مسیر دیتابیس مشخص نیست',
                  textDirection: TextDirection.ltr,
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16),
        Text(
          'پشتیبان‌های موجود',
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: 10),
        if (_backups.isEmpty)
          const Card(
            child: Padding(
              padding: EdgeInsets.all(24),
              child: Text(
                'هنوز پشتیبان محلی ساخته نشده است.',
              ),
            ),
          ),
        for (final backup in _backups)
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: Card(
              child: FutureBuilder<FileStat>(
                future: File(backup).stat(),
                builder: (context, snapshot) {
                  final stat = snapshot.data;
                  return ListTile(
                    leading: const Icon(Icons.storage_outlined),
                    title: Text(p.basename(backup)),
                    subtitle: Text(
                      (stat == null ? '' : _fileSize(stat.size) + ' • ') +
                          backup,
                      textDirection: TextDirection.ltr,
                    ),
                    trailing: OutlinedButton.icon(
                      onPressed: _busy
                          ? null
                          : () => _restoreBackup(backup),
                      icon: const Icon(Icons.restore),
                      label: const Text('بازیابی'),
                    ),
                  );
                },
              ),
            ),
          ),
        const SizedBox(height: 12),
        const Card(
          child: Padding(
            padding: EdgeInsets.all(18),
            child: Text(
              'پشتیبان این صفحه مربوط به دیتابیس محلی Windows/Android است. '
              'برای استقرار چندکاربره PostgreSQL، پشتیبان سرور باید جداگانه نیز فعال باشد.',
            ),
          ),
        ),
      ],
    );
  }
}
