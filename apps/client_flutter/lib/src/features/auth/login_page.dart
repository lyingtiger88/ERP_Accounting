import 'dart:io';

import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:device_info_plus/device_info_plus.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/secure_session_store.dart';
import '../../core/database/local_database.dart';
import '../../core/demo/demo_mode.dart';
import '../../core/demo/local_demo_business_engine.dart';
import '../../core/sync/accounting_sync_service.dart';
import '../dashboard/dashboard_page.dart';
import 'bootstrap_page.dart';

class LoginPage extends StatefulWidget {
  const LoginPage({
    super.key,
    required this.localDatabase,
    this.initialMessage,
  });

  final LocalDatabase localDatabase;
  final String? initialMessage;

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _totpCode = TextEditingController();
  final _pairingCode = TextEditingController();
  ApiClient _apiClient = ApiClient();

  bool _obscurePassword = true;
  bool _rememberCredentials = false;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadRememberedCredentials();
    final message = widget.initialMessage;
    if (message != null && message.isNotEmpty) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(message)),
        );
      });
    }
  }

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    _totpCode.dispose();
    _pairingCode.dispose();
    super.dispose();
  }

  Future<void> _loadRememberedCredentials() async {
    try {
      final enabled = await SecureSessionStore.instance.rememberCredentialsEnabled();
      final credentials = await SecureSessionStore.instance.readSavedCredentials();
      if (!mounted) return;
      setState(() {
        _rememberCredentials = enabled;
        if (credentials != null) {
          _username.text = credentials.$1;
          _password.text = credentials.$2;
        }
      });
    } catch (_) {
      // Credential restoration is optional; login remains available.
    }
  }

  Future<void> _setRememberCredentials(bool value) async {
    setState(() => _rememberCredentials = value);
    if (!value) {
      await SecureSessionStore.instance.forgetCredentials();
    }
  }

  Future<void> _configureServer() async {
    final controller = TextEditingController(
      text: _apiClient.baseUrl,
    );

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('آدرس سرور Beta'),
        content: SizedBox(
          width: 520,
          child: TextField(
            controller: controller,
            autofocus: true,
            textDirection: TextDirection.ltr,
            decoration: const InputDecoration(
              labelText: 'API Base URL',
              hintText: 'https://erp.example.com',
              helperText:
                  'برای دستگاه واقعی، آدرس شبکه یا HTTPS سرور را وارد کنید.',
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
              final value = controller.text.trim();
              final uri = Uri.tryParse(value);
              if (uri == null ||
                  !uri.hasScheme ||
                  uri.host.isEmpty ||
                  (uri.scheme != 'http' && uri.scheme != 'https')) {
                return;
              }
              Navigator.pop(context, true);
            },
            child: const Text('ذخیره'),
          ),
        ],
      ),
    );

    if (confirmed != true) {
      controller.dispose();
      return;
    }

    final value = controller.text.trim().replaceAll(
          RegExp(r'/+$'),
          '',
        );
    controller.dispose();

    await widget.localDatabase.setMeta(
      'api_base_url',
      value,
    );
    await SecureSessionStore.instance.clear();
    ApiClient.configureBaseUrl(value);

    if (!mounted) return;
    setState(() {
      _apiClient = ApiClient();
      _error = null;
    });

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('آدرس سرور ذخیره شد.'),
      ),
    );
  }

  Future<void> _scanPairingCode() async {
    if (!Platform.isAndroid || _busy) return;

    var scanCompleted = false;
    final scanned = await showDialog<Map<String, String>>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('اسکن کد اتصال زر'),
        content: SizedBox(
          width: 340,
          height: 360,
          child: ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: MobileScanner(
              onDetect: (capture) {
                if (scanCompleted || capture.barcodes.isEmpty) return;
                final rawValue = capture.barcodes.first.rawValue?.trim() ?? '';
                final parsed = Uri.tryParse(rawValue);
                var code = rawValue.toUpperCase();
                String? server;
                if (parsed != null &&
                    parsed.scheme == 'zarr' &&
                    parsed.host == 'pair') {
                  code = parsed.queryParameters['code']?.toUpperCase() ?? '';
                  server = parsed.queryParameters['server'];
                }

                final isPairingCode = code.length == 16 &&
                    code.split('').every(
                      (character) => '0123456789ABCDEF'.contains(character),
                    );
                final serverUri = server == null ? null : Uri.tryParse(server);
                final validServer = server == null ||
                    (serverUri != null &&
                        serverUri.host.isNotEmpty &&
                        (serverUri.scheme == 'http' ||
                            serverUri.scheme == 'https'));

                if (isPairingCode && validServer) {
                  scanCompleted = true;
                  Navigator.of(dialogContext).pop({
                    'code': code,
                    if (server != null) 'server': server,
                  });
                }
              },
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text('انصراف'),
          ),
        ],
      ),
    );

    if (!mounted || scanned == null) return;

    final server = scanned['server'];
    if (server != null) {
      var normalizedServer = server.trim();
      while (normalizedServer.endsWith('/')) {
        normalizedServer = normalizedServer.substring(
          0,
          normalizedServer.length - 1,
        );
      }

      var currentServer = ApiClient.configuredBaseUrl;
      while (currentServer.endsWith('/')) {
        currentServer = currentServer.substring(0, currentServer.length - 1);
      }

      if (normalizedServer != currentServer) {
        final serverUri = Uri.parse(normalizedServer);
        final confirmed = await showDialog<bool>(
          context: context,
          builder: (dialogContext) => AlertDialog(
            title: const Text('تغییر آدرس سرور؟'),
            content: Text(
              'کد QR درخواست اتصال به این سرور را دارد:\n\n' +
                  normalizedServer +
                  (serverUri.scheme == 'http'
                      ? '\n\nهشدار: ارتباط HTTP رمزنگاری نشده است.'
                      : '') +
                  '\n\nفقط در صورتی ادامه دهید که این آدرس متعلق به سازمان شماست.',
              textDirection: TextDirection.ltr,
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(dialogContext).pop(false),
                child: const Text('انصراف'),
              ),
              FilledButton(
                onPressed: () => Navigator.of(dialogContext).pop(true),
                child: const Text('تأیید سرور'),
              ),
            ],
          ),
        );

        if (confirmed != true) return;
      }

      await widget.localDatabase.setMeta('api_base_url', normalizedServer);
      ApiClient.configureBaseUrl(normalizedServer);
    }

    setState(() {
      if (server != null) _apiClient = ApiClient();
      _pairingCode.text = scanned['code'] ?? '';
      _error = null;
    });
  }

  Future<String> _currentDeviceName() async {
    final plugin = DeviceInfoPlugin();
    try {
      if (Platform.isAndroid) {
        final info = await plugin.androidInfo;
        final manufacturer = info.manufacturer.trim();
        final model = info.model.trim();
        final label = [manufacturer, model]
            .where((part) => part.isNotEmpty)
            .join(' ');
        return label.isEmpty ? 'Android device' : label;
      }
      if (Platform.isWindows) {
        final info = await plugin.windowsInfo;
        final name = info.computerName.trim();
        return name.isEmpty ? 'Windows PC' : name;
      }
      return '${Platform.operatingSystem} device';
    } catch (_) {
      return Platform.isAndroid ? 'Android device' : 'Windows PC';
    }
  }

  Future<void> _login() async {
    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final result = await _apiClient.login(
        username: _username.text,
        password: _password.text,
        deviceName: await _currentDeviceName(),
        totpCode: _totpCode.text.trim().isEmpty ? null : _totpCode.text.trim(),
        pairingCode: _pairingCode.text.trim().isEmpty ? null : _pairingCode.text.trim(),
      );

      if (!mounted) return;

      if (result.mfaRequired) {
        setState(() {
          _error =
              'این حساب نیاز به مرحله دوم احراز هویت دارد؛ صفحه MFA در گام بعدی فعال می‌شود.';
        });
        return;
      }

      await SecureSessionStore.instance.write(
        result.toSecureSession(),
      );
      if (_rememberCredentials) {
        await SecureSessionStore.instance.saveCredentials(
          username: _username.text.trim(),
          password: _password.text,
        );
      } else {
        await SecureSessionStore.instance.forgetCredentials();
      }

      await widget.localDatabase.cacheUserProfile(
        userId: result.userId,
        companyId: result.companyId,
        displayName: result.displayName,
        role: result.role,
      );

      bool accountsSynced = false;

      try {
        final accounts = await _apiClient.getAccounts(
          bearerToken: result.accessToken,
        );
        final fiscalYears = await _apiClient.getFiscalYears(
          bearerToken: result.accessToken,
        );
        final detailAccounts = await _apiClient.getDetailAccounts(
          bearerToken: result.accessToken,
        );

        await widget.localDatabase.replaceAccounts(
          companyId: result.companyId,
          accounts: accounts,
        );
        await widget.localDatabase.replaceFiscalYears(
          companyId: result.companyId,
          fiscalYears: fiscalYears,
        );

        for (final fiscalYear in fiscalYears) {
          final fiscalYearId = fiscalYear['id'] as String;
          final periods = await _apiClient.getFiscalPeriods(
            bearerToken: result.accessToken,
            fiscalYearId: fiscalYearId,
          );

          await widget.localDatabase.replaceFiscalPeriods(
            companyId: result.companyId,
            fiscalYearId: fiscalYearId,
            periods: periods,
          );
        }

        await widget.localDatabase.replaceDetailAccounts(
          companyId: result.companyId,
          details: detailAccounts,
        );

        await widget.localDatabase.backfillLegacyJournalFiscalYears(
          result.companyId,
        );

        await widget.localDatabase.setMeta(
          'last_account_sync_at',
          DateTime.now().toUtc().toIso8601String(),
        );
        await widget.localDatabase.setMeta(
          'last_accounting_master_sync_at',
          DateTime.now().toUtc().toIso8601String(),
        );

        accountsSynced = true;
      } on ApiException {
        // Successful authentication should not be discarded just because
        // the first cache refresh failed. Existing local data stays intact.
      }

      try {
        await AccountingSyncService(
          localDatabase: widget.localDatabase,
          apiClient: _apiClient,
        ).syncAll(
          companyId: result.companyId,
          bearerToken: result.accessToken,
        );
      } catch (_) {
        // Sync is best-effort on login. Pending changes remain in Outbox.
      }

      if (!mounted) return;

      Navigator.of(context).pushReplacement(
        MaterialPageRoute<void>(
          builder: (_) => DashboardPage(
            displayName: result.displayName,
            role: result.role,
            companyId: result.companyId,
            accessToken: result.accessToken,
            localDatabase: widget.localDatabase,
            accountsSynced: accountsSynced,
          ),
        ),
      );
    } on ApiException catch (error) {
      setState(() => _error = error.message);
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _openDemo() async {
    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await widget.localDatabase.ensureDemoWorkspace();
      await LocalDemoBusinessEngine(
        localDatabase: widget.localDatabase,
      ).initialize();

      if (!mounted) return;

      Navigator.of(context).pushReplacement(
        MaterialPageRoute<void>(
          builder: (_) => DashboardPage(
            displayName: DemoMode.displayName,
            role: DemoMode.role,
            companyId: DemoMode.companyId,
            accessToken: DemoMode.accessToken,
            localDatabase: widget.localDatabase,
            accountsSynced: false,
            isDemoMode: true,
          ),
        ),
      );
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = 'راه‌اندازی حالت تست ناموفق بود: ' + error.toString();
      });
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _openBootstrap() async {
    final created = await Navigator.of(context).push<bool>(
      MaterialPageRoute<bool>(
        builder: (_) => BootstrapPage(apiClient: _apiClient),
      ),
    );

    if (!mounted || created != true) return;

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('شرکت ایجاد شد. حالا با حساب مدیر وارد شوید.'),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    final desktop = width >= 900;

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 1180),
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Row(
                children: [
                  if (desktop)
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.all(40),
                        child: const _BrandPanel(),
                      ),
                    ),
                  Expanded(
                    child: Center(
                      child: ConstrainedBox(
                        constraints: const BoxConstraints(maxWidth: 430),
                        child: Card(
                          child: Padding(
                            padding: const EdgeInsets.all(32),
                            child: Column(
                              mainAxisSize: MainAxisSize.min,
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                Text(
                                  'ورود به حساب',
                                  textAlign: TextAlign.right,
                                  style: Theme.of(context)
                                      .textTheme
                                      .headlineMedium
                                      ?.copyWith(
                                        fontWeight: FontWeight.w700,
                                      ),
                                ),
                                const SizedBox(height: 8),
                                Row(
                                  children: [
                                    Expanded(
                                      child: Text(
                                        'API: ' + _apiClient.baseUrl,
                                        textDirection: TextDirection.ltr,
                                        overflow: TextOverflow.ellipsis,
                                        style: Theme.of(context)
                                            .textTheme
                                            .bodySmall,
                                      ),
                                    ),
                                    IconButton(
                                      tooltip: 'تنظیم آدرس سرور',
                                      onPressed:
                                          _busy ? null : _configureServer,
                                      icon: const Icon(
                                        Icons.dns_outlined,
                                      ),
                                    ),
                                  ],
                                ),
                                const SizedBox(height: 20),
                                TextField(
                                  controller: _username,
                                  enabled: !_busy,
                                  textDirection: TextDirection.ltr,
                                  onSubmitted: (_) => _login(),
                                  decoration: const InputDecoration(
                                    labelText: 'نام کاربری',
                                    prefixIcon: Icon(Icons.person_outline),
                                  ),
                                ),
                                const SizedBox(height: 16),
                                TextField(
                                  controller: _password,
                                  enabled: !_busy,
                                  obscureText: _obscurePassword,
                                  textDirection: TextDirection.ltr,
                                  onSubmitted: (_) => _login(),
                                  decoration: InputDecoration(
                                    labelText: 'رمز عبور',
                                    prefixIcon: const Icon(Icons.lock_outline),
                                    suffixIcon: IconButton(
                                      onPressed: _busy
                                          ? null
                                          : () => setState(
                                                () => _obscurePassword =
                                                    !_obscurePassword,
                                              ),
                                      icon: Icon(
                                        _obscurePassword
                                            ? Icons.visibility_outlined
                                            : Icons.visibility_off_outlined,
                                      ),
                                    ),
                                  ),
                                ),
                                const SizedBox(height: 4),
                                CheckboxListTile(
                                  contentPadding: EdgeInsets.zero,
                                  controlAffinity: ListTileControlAffinity.leading,
                                  value: _rememberCredentials,
                                  onChanged: _busy
                                      ? null
                                      : (value) => _setRememberCredentials(value ?? false),
                                  title: const Text('ذخیره نام کاربری و رمز عبور'),
                                  subtitle: const Text(
                                    'اطلاعات ورود به‌صورت رمزنگاری‌شده روی همین دستگاه نگهداری می‌شود.',
                                    style: TextStyle(fontSize: 12),
                                  ),
                                ),
                                const SizedBox(height: 8),
                                TextField(
                                  controller: _totpCode,
                                  enabled: !_busy,
                                  keyboardType: TextInputType.number,
                                  maxLength: 16,
                                  textDirection: TextDirection.ltr,
                                  decoration: const InputDecoration(
                                    labelText: 'کد تأیید دومرحله‌ای (در صورت فعال بودن)',
                                    prefixIcon: Icon(Icons.security_outlined),
                                    counterText: '',
                                  ),
                                ),
                                const SizedBox(height: 8),
                                TextField(
                                  controller: _pairingCode,
                                  enabled: !_busy,
                                  keyboardType: TextInputType.visiblePassword,
                                  textCapitalization: TextCapitalization.characters,
                                  maxLength: 16,
                                  textDirection: TextDirection.ltr,
                                  decoration: InputDecoration(
                                    labelText: 'کد اتصال دستگاه جدید (اختیاری)',
                                    helperText: 'کد را از دستگاه قبلی دریافت کنید یا QR را اسکن کنید.',
                                    prefixIcon: const Icon(Icons.devices_other_outlined),
                                    suffixIcon: Platform.isAndroid
                                        ? IconButton(
                                            tooltip: 'اسکن QR',
                                            onPressed: _busy ? null : _scanPairingCode,
                                            icon: const Icon(Icons.qr_code_scanner),
                                          )
                                        : null,
                                    counterText: '',
                                  ),
                                ),
                                if (_error != null) ...[
                                  const SizedBox(height: 14),
                                  Text(
                                    _error!,
                                    textAlign: TextAlign.right,
                                    style: TextStyle(
                                      color: Theme.of(context)
                                          .colorScheme
                                          .error,
                                    ),
                                  ),
                                ],
                                const SizedBox(height: 20),
                                FilledButton.icon(
                                  onPressed: _busy ? null : _login,
                                  icon: _busy
                                      ? const SizedBox(
                                          width: 18,
                                          height: 18,
                                          child: CircularProgressIndicator(
                                            strokeWidth: 2,
                                          ),
                                        )
                                      : const Icon(Icons.login),
                                  label: const Padding(
                                    padding:
                                        EdgeInsets.symmetric(vertical: 13),
                                    child: Text('ورود'),
                                  ),
                                ),
                                const SizedBox(height: 12),
                                FilledButton.tonalIcon(
                                  onPressed: _busy ? null : _openDemo,
                                  icon: const Icon(Icons.science_outlined),
                                  label: const Padding(
                                    padding:
                                        EdgeInsets.symmetric(vertical: 12),
                                    child: Text(
                                      'ورود به حالت تست آفلاین',
                                    ),
                                  ),
                                ),
                                const SizedBox(height: 8),
                                const Text(
                                  'بدون سرور • داده نمونه • حداکثر ۵۰۰ سند محلی',
                                  textAlign: TextAlign.center,
                                  style: TextStyle(fontSize: 12),
                                ),
                                const SizedBox(height: 12),
                                const SizedBox(height: 10),
                                TextButton(
                                  onPressed: _busy ? null : _openBootstrap,
                                  child:
                                      const Text('راه‌اندازی اولین شرکت'),
                                ),
                                const SizedBox(height: 4),
                                OutlinedButton.icon(
                                  onPressed: _busy
                                      ? null
                                      : () async {
                                          final uri = Uri.parse('https://kaliia.ir');
                                          await launchUrl(
                                            uri,
                                            mode: LaunchMode.externalApplication,
                                          );
                                        },
                                  icon: const Icon(Icons.person_add_alt_1),
                                  label: const Text('ثبت حساب کاربری در کالیا سافت'),
                                ),
                                const SizedBox(height: 6),
                                const Text(
                                  'برای ایجاد حساب کاربری و مدیریت محصولات به وب‌سایت کالیا سافت مراجعه کنید.',
                                  textAlign: TextAlign.center,
                                  style: TextStyle(fontSize: 12),
                                ),
                                const SizedBox(height: 10),
                                const Text(
                                  '2FA / MFA و Trusted Devices در ادامه Phase 1 تکمیل می‌شوند.',
                                  textAlign: TextAlign.center,
                                  style: TextStyle(fontSize: 12),
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _BrandPanel extends StatelessWidget {
  const _BrandPanel();

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(22),
            child: Image.asset(
              'assets/branding/zarr.webp',
              width: 104,
              height: 104,
              fit: BoxFit.cover,
            ),
          ),
          const SizedBox(height: 24),
          Text(
            'زر | Zarr\nحسابداری و مدیریت منابع روی همه دستگاه‌های شما',
            style: Theme.of(context).textTheme.displaySmall?.copyWith(
                  fontWeight: FontWeight.w800,
                  height: 1.25,
                ),
          ),
          const SizedBox(height: 20),
          Text(
            'Windows • Android • Offline-first • Secure Sync',
            textDirection: TextDirection.ltr,
            style: Theme.of(context).textTheme.titleMedium,
          ),
        ],
      ),
    );
  }
}
