import 'dart:io';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/demo/demo_mode.dart';
import '../../core/sync/accounting_sync_service.dart';
import '../dashboard/dashboard_page.dart';
import 'bootstrap_page.dart';

class LoginPage extends StatefulWidget {
  const LoginPage({
    super.key,
    required this.localDatabase,
  });

  final LocalDatabase localDatabase;

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _apiClient = ApiClient();

  bool _obscurePassword = true;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    super.dispose();
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
        deviceName: Platform.isAndroid ? 'Android Device' : 'Windows PC',
      );

      if (!mounted) return;

      if (result.mfaRequired) {
        setState(() {
          _error =
              'این حساب نیاز به مرحله دوم احراز هویت دارد؛ صفحه MFA در گام بعدی فعال می‌شود.';
        });
        return;
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
                                Text(
                                  'API: ' + _apiClient.baseUrl,
                                  textAlign: TextAlign.right,
                                  style: Theme.of(context).textTheme.bodySmall,
                                ),
                                const SizedBox(height: 28),
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
                                OutlinedButton.icon(
                                  onPressed: _busy
                                      ? null
                                      : () {
                                          ScaffoldMessenger.of(context)
                                              .showSnackBar(
                                            const SnackBar(
                                              content: Text(
                                                'QR Pairing در مرحله امنیت فعال خواهد شد.',
                                              ),
                                            ),
                                          );
                                        },
                                  icon: const Icon(Icons.qr_code_scanner),
                                  label: const Text('اتصال اختیاری با QR'),
                                ),
                                const SizedBox(height: 10),
                                TextButton(
                                  onPressed: _busy ? null : _openBootstrap,
                                  child:
                                      const Text('راه‌اندازی اولین شرکت'),
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
          Icon(
            Icons.account_balance_rounded,
            size: 58,
            color: Theme.of(context).colorScheme.primary,
          ),
          const SizedBox(height: 24),
          Text(
            'حسابداری و مدیریت منابع،\nروی همه دستگاه‌های شما',
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
