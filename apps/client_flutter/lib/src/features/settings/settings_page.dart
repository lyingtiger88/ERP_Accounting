import 'dart:io';

import 'package:flutter/material.dart';
import 'package:path/path.dart' as p;
import 'package:url_launcher/url_launcher.dart';
import 'package:qr_flutter/qr_flutter.dart';
import 'package:timezone/data/latest_all.dart' as tzdata;
import 'package:timezone/timezone.dart' as tz;
import '../../licensing/zarr_license_page.dart';
import '../../core/app_version.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/secure_session_store.dart';
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
  bool _loadingDevices = false;
  bool _loadingSessions = false;
  String? _sessionLoadError;
  String _timeZoneId = 'Asia/Tehran';
  bool _timeZoneReady = false;
  bool _loadingAudit = false;
  bool _loadingMfa = false;
  bool _mfaEnabled = false;
  bool _busy = false;
  List<Map<String, dynamic>> _users = const [];
  List<Map<String, dynamic>> _devices = const [];
  List<Map<String, dynamic>> _sessions = const [];
  List<Map<String, dynamic>> _audit = const [];
  List<String> _backups = const [];

  bool get _isDemo => DemoMode.isDemoToken(widget.accessToken);

  bool get _canManageUsers {
    final normalizedRole = widget.role.trim().toLowerCase();
    return !_isDemo &&
        (normalizedRole == 'owner' ||
         normalizedRole == 'administrator' ||
         normalizedRole == 'admin');
  }

  @override
  void initState() {
    super.initState();
    tzdata.initializeTimeZones();
    _loadTimeZone();
    _loadAll();
  }

  static const _timeZones = <String, String>{
    'Asia/Tehran': 'تهران',
    'Europe/Berlin': 'برلین',
    'Europe/London': 'لندن',
    'Europe/Paris': 'پاریس',
    'Europe/Istanbul': 'استانبول',
    'Asia/Dubai': 'دبی',
    'Asia/Riyadh': 'ریاض',
    'Asia/Kabul': 'کابل',
    'Asia/Karachi': 'کراچی',
    'Asia/Kolkata': 'کلکته',
    'Asia/Tokyo': 'توکیو',
    'Asia/Shanghai': 'شانگهای',
    'Asia/Singapore': 'سنگاپور',
    'America/New_York': 'نیویورک',
    'America/Chicago': 'شیکاگو',
    'America/Los_Angeles': 'لس‌آنجلس',
    'Australia/Sydney': 'سیدنی',
    'UTC': 'UTC',
  };

  Future<void> _loadTimeZone() async {
    final saved = await SecureSessionStore.instance.readTimeZoneId();
    if (!mounted) return;
    setState(() {
      if (saved != null && _timeZones.containsKey(saved)) {
        _timeZoneId = saved;
      }
      _timeZoneReady = true;
    });
  }

  Future<void> _saveTimeZone(String? value) async {
    if (value == null || !_timeZones.containsKey(value)) return;
    setState(() => _timeZoneId = value);
    await SecureSessionStore.instance.writeTimeZoneId(value);
    if (mounted) _message('منطقه زمانی ذخیره شد.');
  }

  String _formatRegionalTime(dynamic value) {
    if (value == null) return 'ثبت نشده';
    final parsed = DateTime.tryParse(value.toString());
    if (parsed == null) return value.toString();
    final local = tz.TZDateTime.from(parsed.toUtc(), tz.getLocation(_timeZoneId));
    String two(int n) => n.toString().padLeft(2, '0');
    return '${two(local.year)}-${two(local.month)}-${two(local.day)} '
        '${two(local.hour)}:${two(local.minute)}:${two(local.second)}';
  }

  Widget _locationTimeZoneTab() {
    return ListView(
      padding: const EdgeInsets.all(20),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text('منطقه زمانی مختصات مکانی', style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 8),
                const Text(
                  'زمان رویدادها و اتصال‌ها در برنامه بر اساس منطقه زمانی انتخاب‌شده نمایش داده می‌شود. '
                  'زمان ذخیره‌شده در سرور همچنان UTC باقی می‌ماند و فقط نمایش محلی تغییر می‌کند.',
                ),
                const SizedBox(height: 14),
                DropdownButtonFormField<String>(
                  value: _timeZoneId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'منطقه زمانی',
                    prefixIcon: Icon(Icons.public),
                  ),
                  items: [
                    for (final entry in _timeZones.entries)
                      DropdownMenuItem(
                        value: entry.key,
                        child: Text('${entry.value} — ${entry.key}', textDirection: TextDirection.ltr),
                      ),
                  ],
                  onChanged: !_timeZoneReady || _busy ? null : _saveTimeZone,
                ),
                const SizedBox(height: 10),
                Text(
                  'زمان فعلی: ${_formatRegionalTime(DateTime.now().toUtc().toIso8601String())}',
                  textDirection: TextDirection.ltr,
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Future<void> _loadAll() async {
    await Future.wait([
      _loadUsers(),
      _loadDevices(),
      _loadSessions(),
      _loadMfa(),
      _loadAudit(),
      _loadBackups(),
    ]);
  }

  Future<void> _loadMfa() async {
    if (_isDemo) return;
    if (mounted) setState(() => _loadingMfa = true);
    try {
      final status = await _api.getMfaStatus(bearerToken: widget.accessToken);
      if (!mounted) return;
      setState(() => _mfaEnabled = status['mfaEnabled'] as bool? ?? false);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _loadingMfa = false);
    }
  }

  Future<void> _setupMfa() async {
    setState(() => _busy = true);
    try {
      final setup = await _api.beginMfaSetup(bearerToken: widget.accessToken);
      final code = TextEditingController();
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('راه‌اندازی احراز هویت دومرحله‌ای'),
          content: SizedBox(
            width: 560,
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Text('در برنامه Authenticator گزینه افزودن حساب را انتخاب کنید و کلید زیر را به‌صورت دستی وارد کنید:'),
                  const SizedBox(height: 12),
                  SelectableText(setup['secret'].toString(), textDirection: TextDirection.ltr),
                  const SizedBox(height: 12),
                  const Text('نشانی استاندارد ورود دستی:'),
                  SelectableText(setup['otpauthUri'].toString(), textDirection: TextDirection.ltr),
                  const SizedBox(height: 16),
                  TextField(
                    controller: code,
                    autofocus: true,
                    keyboardType: TextInputType.number,
                    maxLength: 6,
                    textDirection: TextDirection.ltr,
                    decoration: const InputDecoration(labelText: 'کد شش‌رقمی برنامه Authenticator'),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
            FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('تأیید و فعال‌سازی')),
          ],
        ),
      );
      if (confirmed != true) {
        code.dispose();
        return;
      }
      final result = await _api.enableMfa(
        bearerToken: widget.accessToken,
        code: code.text.trim(),
      );
      code.dispose();
      if (!mounted) return;
      setState(() => _mfaEnabled = true);
      final recovery = (result['recoveryCodes'] as List<dynamic>? ?? const [])
          .map((value) => value.toString())
          .toList(growable: false);
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('کدهای بازیابی — فقط همین یک‌بار'),
          content: SizedBox(
            width: 420,
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Text('این کدها را در مکانی امن و خارج از دستگاه ذخیره کنید. هر کد فقط یک‌بار قابل استفاده است.'),
                  const SizedBox(height: 12),
                  for (final value in recovery)
                    SelectableText(value, textDirection: TextDirection.ltr),
                ],
              ),
            ),
          ),
          actions: [FilledButton(onPressed: () => Navigator.pop(context), child: const Text('ذخیره کردم'))],
        ),
      );
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _disableMfa() async {
    final password = TextEditingController();
    final code = TextEditingController();
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('غیرفعال‌سازی احراز هویت دومرحله‌ای'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(controller: password, obscureText: true, decoration: const InputDecoration(labelText: 'رمز عبور فعلی')),
            const SizedBox(height: 12),
            TextField(controller: code, keyboardType: TextInputType.number, textDirection: TextDirection.ltr, decoration: const InputDecoration(labelText: 'کد Authenticator یا کد بازیابی')),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('غیرفعال‌سازی')),
        ],
      ),
    );
    if (confirmed != true) {
      password.dispose();
      code.dispose();
      return;
    }
    setState(() => _busy = true);
    try {
      await _api.disableMfa(
        bearerToken: widget.accessToken,
        password: password.text,
        code: code.text.trim(),
      );
      if (mounted) {
        setState(() => _mfaEnabled = false);
        _message('احراز هویت دومرحله‌ای غیرفعال شد.');
      }
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      password.dispose();
      code.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Widget _mfaTab() {
    if (_loadingMfa) return const Center(child: CircularProgressIndicator());
    return ListView(
      padding: const EdgeInsets.all(20),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    Icon(_mfaEnabled ? Icons.verified_user : Icons.security_outlined),
                    const SizedBox(width: 10),
                    Expanded(child: Text('احراز هویت دومرحله‌ای (TOTP)', style: Theme.of(context).textTheme.titleMedium)),
                    Chip(label: Text(_mfaEnabled ? 'فعال' : 'غیرفعال')),
                  ],
                ),
                const SizedBox(height: 12),
                const Text('سازگار با Google Authenticator و Microsoft Authenticator. هنگام ورود، علاوه بر رمز عبور کد شش‌رقمی یا یک کد بازیابی لازم است.'),
                const SizedBox(height: 16),
                if (!_mfaEnabled)
                  FilledButton.icon(
                    onPressed: _busy ? null : _setupMfa,
                    icon: const Icon(Icons.phonelink_lock_outlined),
                    label: const Text('راه‌اندازی و فعال‌سازی'),
                  )
                else
                  OutlinedButton.icon(
                    onPressed: _busy ? null : _disableMfa,
                    icon: const Icon(Icons.lock_open_outlined),
                    label: const Text('غیرفعال‌سازی با تأیید مجدد'),
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Future<void> _loadDevices() async {
    if (_isDemo) return;

    if (mounted) {
      setState(() => _loadingDevices = true);
    }

    try {
      final devices = await _api.getTrustedDevices(
        bearerToken: widget.accessToken,
      );

      if (!mounted) return;
      setState(() => _devices = devices);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) {
        setState(() => _loadingDevices = false);
      }
    }
  }

  Future<void> _loadSessions() async {
    if (_isDemo) return;
    if (mounted) setState(() { _loadingSessions = true; _sessionLoadError = null; });
    try {
      final sessions = await _api.getActiveSessions(
        bearerToken: widget.accessToken,
      );
      if (!mounted) return;
      setState(() => _sessions = sessions);
    } on ApiException catch (error) {
      if (mounted) setState(() => _sessionLoadError = error.message);
    } finally {
      if (mounted) setState(() => _loadingSessions = false);
    }
  }

  Future<void> _revokeAllSessions() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('حذف کامل نشست‌ها'),
        content: const Text(
          'تمام نشست‌های دیگر این حساب، از جمله گوشی‌های جفت‌شده با QR، باطل می‌شوند. '
          'نشست همین دستگاهی که اکنون با آن کار می‌کنید حفظ خواهد شد. ادامه می‌دهید؟',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('خروج از سایر دستگاه‌ها'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;

    setState(() => _busy = true);
    try {
      final revokedCount = await _api.revokeAllSessions(
        bearerToken: widget.accessToken,
      );
      _message('$revokedCount نشست دیگر باطل شد؛ نشست فعلی حفظ شده است.');
      await Future.wait([_loadSessions(), _loadDevices()]);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _revokeDevice(
    Map<String, dynamic> device,
  ) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('لغو دسترسی دستگاه'),
        content: Text(
          'Sessionهای دستگاه «' +
              device['deviceName'].toString() +
              '» باطل می‌شوند. ادامه می‌دهید؟',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('لغو دسترسی'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    setState(() => _busy = true);
    try {
      await _api.revokeTrustedDevice(
        bearerToken: widget.accessToken,
        deviceId: device['id'].toString(),
      );
      _message('دسترسی دستگاه و Sessionهای آن باطل شد.');
      await Future.wait([_loadDevices(), _loadSessions()]);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _deleteDevice(Map<String, dynamic> device) async {
    final name = device['deviceName']?.toString() ?? 'دستگاه ناشناس';
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('حذف کامل دستگاه'),
        content: Text(
          'دستگاه «$name» از فهرست این حساب حذف می‌شود. نشست‌های مرتبط باطل و از اتصال به این دستگاه جدا می‌شوند؛ سوابق امنیتی حفظ خواهند شد. برای استفاده مجدد، دستگاه باید دوباره ثبت شود. ادامه می‌دهید؟',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(
              backgroundColor: Theme.of(dialogContext).colorScheme.error,
            ),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('حذف کامل دستگاه'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;

    setState(() => _busy = true);
    try {
      await _api.deleteTrustedDevice(
        bearerToken: widget.accessToken,
        deviceId: device['id'].toString(),
      );
      _message('دستگاه حذف شد و نشست‌های مرتبط باطل شدند.');
      await Future.wait([_loadDevices(), _loadSessions()]);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _loadAudit() async {
    if (!_canManageUsers) return;

    if (mounted) {
      setState(() => _loadingAudit = true);
    }

    try {
      final rows = await _api.getSecurityAudit(
        bearerToken: widget.accessToken,
      );

      if (!mounted) return;
      setState(() => _audit = rows);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) {
        setState(() => _loadingAudit = false);
      }
    }
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
          'این کار دیتابیس محلی فعلی را با پشتیبان زیر جایگزین می‌کند:\n\n' +
              p.basename(path) +
              '\n' +
              _fileSize(stat.size) +
              '\n\nقبل از جایگزینی، نسخه ایمنی خودکار ساخته می‌شود.',
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
      await SecureSessionStore.instance.clear();
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
    final showDevices = !_isDemo;
    final showMfa = !_isDemo;
    final tabs =
        1 +
        (showMfa ? 1 : 0) +
        (_canManageUsers ? 2 : 0) +
        (showDevices ? 2 : 0) +
        2;

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
                  if (showMfa)
                    const Tab(
                      icon: Icon(Icons.security_outlined),
                      text: 'احراز هویت دومرحله‌ای',
                    ),
                  if (showDevices) ...[
                    const Tab(icon: Icon(Icons.devices_outlined), text: 'دستگاه‌ها'),
                    const Tab(icon: Icon(Icons.login_outlined), text: 'نشست‌ها'),
                  ],
                  const Tab(icon: Icon(Icons.public_outlined), text: 'مختصات مکانی'),
                  if (_canManageUsers)
                    const Tab(
                      icon: Icon(Icons.security_outlined),
                      text: 'رویدادهای امنیتی',
                    ),
                  const Tab(
                    icon: Icon(Icons.backup_outlined),
                    text: 'پشتیبان محلی',
                  ),
                  const Tab(
                    icon: Icon(Icons.info_outline),
                    text: 'درباره نرم‌افزار',
                  ),
                ],
              ),
              Expanded(
                child: TabBarView(
                  children: [
                    if (_canManageUsers) _usersTab(),
                    if (showMfa) _mfaTab(),
                    if (showDevices) _devicesTab(),
                    if (showDevices) _sessionsTab(),
                    _locationTimeZoneTab(),
                    if (_canManageUsers) _auditTab(),
                    _backupTab(),
                    _aboutTab(),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _aboutTab() {
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 560),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.account_balance_outlined, size: 52),
                  const SizedBox(height: 12),
                  Text(
                    'زر | Zarr — حسابداری کالیا سافت',
                    style: Theme.of(context).textTheme.titleLarge,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'برای اطلاعات محصولات، ثبت حساب کاربری، پشتیبانی و مدیریت اشتراک به وب‌سایت رسمی کالیا سافت مراجعه کنید.',
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 18),
                  Container(
                    width: double.infinity,
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(
                      border: Border.all(
                        color: Theme.of(context).colorScheme.outlineVariant,
                      ),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: const Column(
                      children: [
                        Text(
                          'نسخه نرم‌افزار',
                          style: TextStyle(fontWeight: FontWeight.w600),
                        ),
                        SizedBox(height: 6),
                        SelectableText(
                          ZarrAppVersion.display,
                          textDirection: TextDirection.ltr,
                        ),
                        SizedBox(height: 4),
                        Text(
                          ZarrAppVersion.platforms,
                          textAlign: TextAlign.center,
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 18),
                  SelectableText(
                    'https://kaliia.ir',
                    textDirection: TextDirection.ltr,
                    style: TextStyle(color: Theme.of(context).colorScheme.primary),
                  ),
                  const SizedBox(height: 16),
                  FilledButton.tonalIcon(
                    onPressed: () => Navigator.of(context).push(
                      MaterialPageRoute<void>(
                        builder: (_) => ZarrLicensePage(
                          erpAccessToken: widget.accessToken,
                          canLinkCompany: widget.role == 'Owner' && !_isDemo,
                        ),
                      ),
                    ),
                    icon: const Icon(Icons.verified_user_outlined),
                    label: const Text('حساب و وضعیت اشتراک زر'),
                  ),
                  const SizedBox(height: 12),
                  FilledButton.icon(
                    onPressed: () => launchUrl(
                      Uri.parse('https://kaliia.ir'),
                      mode: LaunchMode.externalApplication,
                    ),
                    icon: const Icon(Icons.open_in_new),
                    label: const Text('بازکردن وب‌سایت کالیا سافت'),
                  ),
                ],
              ),
            ),
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

  Future<void> _createDevicePairingCode() async {
    setState(() => _busy = true);
    try {
      final result = await _api.createDevicePairingCode(
        bearerToken: widget.accessToken,
      );
      if (!mounted) return;
      final code = result['code']?.toString() ?? '';
      final expiresAt = result['expiresAt']?.toString() ?? '';
      final configuredServer = ApiClient.configuredBaseUrl;
      final serverUri = Uri.tryParse(configuredServer);
      final serverIsLoopback = serverUri == null ||
          const {'localhost', '127.0.0.1', '::1', '10.0.2.2'}
              .contains(serverUri.host.toLowerCase());
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('کد یک‌بارمصرف اتصال دستگاه'),
          content: SizedBox(
            width: 420,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text(
                  'در دستگاه جدید، همین کد را در صفحه ورود وارد کنید. ورود همچنان به نام کاربری، رمز عبور و در صورت فعال بودن MFA به کد دومرحله‌ای نیاز دارد.',
                ),
                const SizedBox(height: 18),
                Center(
                  child: QrImageView(
                    data: Uri(
                      scheme: 'zarr',
                      host: 'pair',
                      queryParameters: {
                        if (!serverIsLoopback) 'server': configuredServer,
                        'code': code,
                      },
                    ).toString(),
                    version: QrVersions.auto,
                    size: 220,
                    backgroundColor: Colors.white,
                  ),
                ),
                const SizedBox(height: 14),
                SelectableText(
                  code,
                  textAlign: TextAlign.center,
                  textDirection: TextDirection.ltr,
                  style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.bold,
                    letterSpacing: 3,
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  'اعتبار تا: ' + expiresAt,
                  textDirection: TextDirection.ltr,
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                if (serverIsLoopback) ...[
                  const SizedBox(height: 8),
                  const Text(
                    'آدرس سرور این دستگاه محلی است. در دستگاه جدید، آدرس شبکه یا HTTPS سرور را جداگانه تنظیم کنید؛ QR فقط کد اتصال را منتقل می‌کند.',
                    textAlign: TextAlign.right,
                    style: TextStyle(color: Colors.orange),
                  ),
                ],
                const SizedBox(height: 8),
                const Text(
                  'با ساخت کد جدید، کد قبلی فوراً باطل می‌شود. این کد فقط یک‌بار قابل استفاده است.',
                  textAlign: TextAlign.right,
                ),
              ],
            ),
          ),
          actions: [
            FilledButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('متوجه شدم'),
            ),
          ],
        ),
      );
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _setDeviceBlacklist(
    Map<String, dynamic> device, bool blacklist,
  ) async {
    final deviceId = device['id'].toString();
    final name = device['deviceName']?.toString() ?? 'دستگاه';
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(blacklist ? 'قرار دادن در فهرست سیاه' : 'خروج از فهرست سیاه'),
        content: Text(blacklist
            ? 'این دستگاه مسدود می‌شود، نشست‌های آن باطل می‌شوند و حتی با QR نیز اجازه ورود نخواهد داشت: $name'
            : 'مسدودیت دستگاه برداشته می‌شود؛ اما برای ورود مجدد همچنان باید QR جدید جفت‌سازی شود. ادامه می‌دهید؟'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
          FilledButton(onPressed: () => Navigator.pop(context, true),
              child: Text(blacklist ? 'مسدود کردن' : 'رفع مسدودیت')),
        ],
      ),
    );
    if (confirmed != true) return;
    setState(() => _busy = true);
    try {
      if (blacklist) {
        await _api.blacklistTrustedDevice(
          bearerToken: widget.accessToken, deviceId: deviceId,
        );
      } else {
        await _api.unblacklistTrustedDevice(
          bearerToken: widget.accessToken, deviceId: deviceId,
        );
      }
      await Future.wait([_loadDevices(), _loadSessions(), _loadAudit()]);
      if (mounted) _message(blacklist ? 'دستگاه مسدود شد.' : 'مسدودیت برداشته شد؛ ورود مجدد نیازمند QR است.');
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Widget _devicesTab() {
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: Align(
            alignment: Alignment.centerRight,
            child: FilledButton.icon(
              onPressed: _busy ? null : _createDevicePairingCode,
              icon: const Icon(Icons.add_link),
              label: const Text('ایجاد کد اتصال دستگاه جدید'),
            ),
          ),
        ),
        Expanded(
          child: _loadingDevices
              ? const Center(child: CircularProgressIndicator())
              : _devices.isEmpty
                  ? const Center(child: Text('دستگاه ثبت‌شده‌ای وجود ندارد.'))
                  : ListView.separated(
                      padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
                      itemCount: _devices.length,
                      separatorBuilder: (_, _) => const SizedBox(height: 10),
                      itemBuilder: (context, index) {
                        final device = _devices[index];
                        final state = device['trustState']?.toString() ?? 'Pending';
                        final revoked = state == 'Revoked';
                        final blacklisted = device['isBlacklisted'] == true;
                        return Card(
                          child: ListTile(
                            leading: CircleAvatar(
                              child: Icon(device['platform']?.toString().toLowerCase() == 'android'
                                  ? Icons.phone_android_outlined : Icons.computer_outlined),
                            ),
                            title: Text(device['deviceName'].toString()),
                            subtitle: Text(
                              device['platform'].toString() + ' • ' +
                              (blacklisted ? 'مسدود در فهرست سیاه' : revoked ? 'لغو شده' : state == 'Trusted' ? 'قابل اعتماد' : 'در انتظار اعتماد') +
                              (device['lastSeenAt'] == null ? '' :
                                ' • آخرین فعالیت: ' + _formatRegionalTime(device['lastSeenAt'])),
                            ),
                            trailing: blacklisted && _canManageUsers
                                ? TextButton.icon(
                                    onPressed: _busy
                                        ? null
                                        : () => _setDeviceBlacklist(device, false),
                                    icon: const Icon(Icons.lock_open),
                                    label: const Text('رفع مسدودیت'),
                                  )
                                : PopupMenuButton<String>(
                              enabled: !_busy,
                              tooltip: 'عملیات دستگاه',
                              onSelected: (action) {
                                if (action == 'blacklist') {
                                  _setDeviceBlacklist(device, true);
                                } else if (action == 'unblacklist') {
                                  _setDeviceBlacklist(device, false);
                                } else if (action == 'revoke' && !revoked) {
                                  _revokeDevice(device);
                                } else if (action == 'delete') {
                                  _deleteDevice(device);
                                }
                              },
                              itemBuilder: (context) => [
                                if (_canManageUsers && !blacklisted && device['requiresRePairing'] != true)
                                  const PopupMenuItem(value: 'blacklist', child: ListTile(dense: true, leading: Icon(Icons.block), title: Text('قرار دادن در فهرست سیاه'))),
                                if (_canManageUsers && blacklisted)
                                  const PopupMenuItem(value: 'unblacklist', child: ListTile(dense: true, leading: Icon(Icons.check_circle_outline), title: Text('رفع مسدودیت'))),
                                if (!revoked && !blacklisted)
                                  const PopupMenuItem(
                                    value: 'revoke',
                                    child: ListTile(
                                      dense: true,
                                      leading: Icon(Icons.phonelink_erase_outlined),
                                      title: Text('لغو دسترسی'),
                                    ),
                                  ),
                                if (!blacklisted)
                                  const PopupMenuItem(
                                  value: 'delete',
                                  child: ListTile(
                                    dense: true,
                                    leading: Icon(Icons.delete_outline),
                                    title: Text('حذف کامل دستگاه'),
                                  ),
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

  Widget _sessionsTab() {
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 16, 20, 8),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  'نشست‌های فعال — ${_sessions.length}',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              IconButton(
                tooltip: 'بازخوانی',
                onPressed: _busy || _loadingSessions ? null : _loadSessions,
                icon: const Icon(Icons.refresh),
              ),
            ],
          ),
        ),
        Expanded(
          child: _loadingSessions
              ? const Center(child: CircularProgressIndicator())
              : _sessionLoadError != null
                  ? Center(child: Text('خطا در دریافت نشست‌ها: $_sessionLoadError'))
                  : _sessions.isEmpty
                  ? const Center(child: Text('سرور هیچ نشست فعالی گزارش نکرد؛ اتصال را بازخوانی کنید.'))
                  : ListView.separated(
                      padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
                      itemCount: _sessions.length,
                      separatorBuilder: (_, _) => const SizedBox(height: 10),
                      itemBuilder: (context, index) {
                        final session = _sessions[index];
                        final device = session['deviceName']?.toString() ?? 'دستگاه ناشناس';
                        final platform = session['platform']?.toString() ?? 'نامشخص';
                        return Card(
                          child: Padding(
                            padding: const EdgeInsets.all(14),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                Row(
                                  children: [
                                    const Icon(Icons.devices_outlined),
                                    const SizedBox(width: 8),
                                    Expanded(child: Text(device, style: Theme.of(context).textTheme.titleMedium)),
                                    if (session['isCurrent'] == true)
                                      const Chip(label: Text('نشست جاری')),
                                  ],
                                ),
                                const Divider(height: 22),
                                _sessionInfoRow('پلتفرم', platform),
                                _sessionInfoRow('زمان اتصال', _formatRegionalTime(session['createdAt'])),
                                _sessionInfoRow('آخرین تمدید', _formatRegionalTime(session['lastRefreshedAt'])),
                                _sessionInfoRow('انقضای دسترسی', _formatRegionalTime(session['accessExpiresAt'])),
                                _sessionInfoRow('انقضای نشست', _formatRegionalTime(session['refreshExpiresAt'])),
                                _sessionInfoRow('منطقه زمانی', _timeZoneId),
                              ],
                            ),
                          ),
                        );
                      },
                    ),
        ),
        if (_sessions.isNotEmpty)
          Padding(
            padding: const EdgeInsets.all(16),
            child: SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: _busy ? null : _revokeAllSessions,
                icon: const Icon(Icons.delete_sweep_outlined),
                label: const Text('حذف کامل همه نشست‌ها'),
              ),
            ),
          ),
      ],
    );
  }

  Widget _sessionInfoRow(String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(width: 120, child: Text(label, style: Theme.of(context).textTheme.bodySmall)),
          Expanded(child: SelectableText(value, textDirection: TextDirection.ltr)),
        ],
      ),
    );
  }

  Widget _auditTab() {
    if (_loadingAudit) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_audit.isEmpty) {
      return const Center(
        child: Text('رویداد امنیتی ثبت نشده است.'),
      );
    }

    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: Align(
            alignment: Alignment.centerRight,
            child: OutlinedButton.icon(
              onPressed: _busy ? null : _loadAudit,
              icon: const Icon(Icons.refresh),
              label: const Text('بازخوانی'),
            ),
          ),
        ),
        Expanded(
          child: ListView.separated(
            padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
            itemCount: _audit.length,
            separatorBuilder: (_, _) => const SizedBox(height: 8),
            itemBuilder: (context, index) {
              final row = _audit[index];
              final success =
                  row['outcome']?.toString() == 'SUCCESS';

              return Card(
                child: ListTile(
                  leading: CircleAvatar(
                    child: Icon(
                      success
                          ? Icons.verified_user_outlined
                          : Icons.gpp_bad_outlined,
                    ),
                  ),
                  title: Text(
                    row['eventType'].toString() +
                        ' • ' +
                        row['outcome'].toString(),
                  ),
                  subtitle: Text(
                    (row['username']?.toString() ?? 'سیستم') +
                        (row['details'] == null
                            ? ''
                            : '\n' + row['details'].toString()) +
                        '\n' +
                        row['createdAt'].toString(),
                  ),
                  isThreeLine: true,
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
