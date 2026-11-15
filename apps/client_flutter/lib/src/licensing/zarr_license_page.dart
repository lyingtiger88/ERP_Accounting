import 'package:flutter/material.dart';
import '../core/api/api_client.dart';
import 'kaliia_entitlement.dart';
import 'zarr_account_service.dart';

/// Zarr subscription management is independent of the ERP API login.
/// Full write enforcement must still be implemented on the accounting API.
class ZarrLicensePage extends StatefulWidget {
  const ZarrLicensePage({
    super.key,
    required this.erpAccessToken,
    required this.canLinkCompany,
  });
  final String erpAccessToken;
  final bool canLinkCompany;
  @override
  State<ZarrLicensePage> createState() => _ZarrLicensePageState();
}

class _ZarrLicensePageState extends State<ZarrLicensePage> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _service = ZarrAccountService();
  final _api = ApiClient();
  bool? _companyLinked;
  KaliiaEntitlement? _license;
  bool _busy = false;
  bool _signedIn = false;
  String? _message;

  @override
  void initState() {
    super.initState();
    _loadStatus();
  }
  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }
  Future<void> _run(Future<void> Function() task) async {
    setState(() { _busy = true; _message = null; });
    try {
      await task();
    } catch (_) {
      if (mounted) setState(() {
        _message = 'درخواست ناموفق بود. اتصال اینترنت، اطلاعات ورود و تأیید ایمیل را بررسی کنید.';
      });
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }
  Future<void> _loadStatus() => _run(() async {
    final account = await _service.accountEmail();
    if (account == null || account.isEmpty) return;
    final entitlement = await _service.status();
    if (!mounted) return;
    setState(() {
      _signedIn = true;
      _email.text = account;
      _license = entitlement;
    });
  });
  Future<void> _login() => _run(() async {
    await _service.signIn(_email.text, _password.text);
    _password.clear();
    final entitlement = await _service.status();
    if (!mounted) return;
    setState(() { _signedIn = true; _license = entitlement; });
  });
  Future<void> _startTrial() => _run(() async {
    final entitlement = await _service.startTrial();
    if (mounted) setState(() => _license = entitlement);
  });
  Future<void> _linkCompany() => _run(() async {
    final token = await _service.verifiedKaliiaAccessToken();
    await _api.linkZarrCompany(
      bearerToken: widget.erpAccessToken,
      kaliiaAccessToken: token,
    );
    if (!mounted) return;
    setState(() {
      _companyLinked = true;
      _message = 'حساب کالیا سافت با شرکت حسابداری زر پیوند داده شد.';
    });
  });
  Future<void> _checkCompany() => _run(() async {
    final result = await _api.zarrCompanyLicenseStatus(
      bearerToken: widget.erpAccessToken,
    );
    if (!mounted) return;
    setState(() {
      _companyLinked = result['linked'] == true;
      _message = result['canWrite'] == true
          ? 'مجوز نوشتن شرکت توسط سرور تأیید شد.'
          : 'مجوز نوشتن شرکت هنوز از سوی سرور تأیید نشده است.';
    });
  });

  Future<void> _logout() => _run(() async {
    await _service.signOut();
    if (mounted) setState(() { _signedIn = false; _license = null; });
  });
  String _statusText() {
    final license = _license;
    if (license == null) return 'وضعیت مجوز دریافت نشده است.';
    switch (license.status) {
      case 'active': return 'مجوز فعال — ' + license.daysRemaining.toString() + ' روز باقی‌مانده';
      case 'expired': return 'اشتراک پایان یافته است؛ حالت فقط‌خواندنی موردنیاز است.';
      case 'trial_available': return 'دوره آزمایشی ۴۵روزه هنوز آغاز نشده است.';
      case 'revoked': return 'مجوز لغو شده است.';
      case 'suspended': return 'مجوز تعلیق شده است.';
      default: return 'وضعیت مجوز: ' + license.status;
    }
  }
  @override
  Widget build(BuildContext context) => Directionality(
    textDirection: TextDirection.rtl,
    child: Scaffold(
      appBar: AppBar(title: const Text('حساب و اشتراک زر | Zarr')),
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 540),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.verified_user_outlined, size: 52),
                    const SizedBox(height: 12),
                    Text('حساب کالیا سافت — محصول زر',
                      style: Theme.of(context).textTheme.titleLarge,
                      textAlign: TextAlign.center),
                    const SizedBox(height: 12),
                    const Text(
                      'حساب کالیا سافت با حساب داخلی سرور حسابداری متفاوت است. '
                      'مدیریت اشتراک زر مستقل از سایر محصولات انجام می‌شود.',
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 22),
                    if (!_signedIn) ...[
                      TextField(
                        controller: _email,
                        enabled: !_busy,
                        keyboardType: TextInputType.emailAddress,
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(labelText: 'ایمیل حساب کالیا سافت'),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: _password,
                        enabled: !_busy,
                        obscureText: true,
                        decoration: const InputDecoration(labelText: 'رمز عبور کالیا سافت'),
                      ),
                      const SizedBox(height: 14),
                      FilledButton(
                        onPressed: _busy ? null : _login,
                        child: const Text('ورود و بررسی اشتراک'),
                      ),
                    ] else ...[
                      SelectableText(_email.text, textAlign: TextAlign.center),
                      const SizedBox(height: 12),
                      Text(_statusText(), textAlign: TextAlign.center),
                      const SizedBox(height: 12),
                      if (_license?.expiresAt != null)
                        Text('تاریخ پایان: ' + _license!.expiresAt!.toLocal().toString(),
                          textAlign: TextAlign.center),
                      if (_license?.trialAvailable == true) ...[
                        const SizedBox(height: 12),
                        FilledButton(
                          onPressed: _busy ? null : _startTrial,
                          child: const Text('شروع دوره آزمایشی ۴۵روزه زر'),
                        ),
                      ],
                      const SizedBox(height: 8),
                      if (widget.canLinkCompany) ...[
                        FilledButton.tonalIcon(
                          onPressed: _busy ? null : _linkCompany,
                          icon: const Icon(Icons.link),
                          label: const Text('اتصال حساب کالیا سافت به شرکت زر'),
                        ),
                        const SizedBox(height: 6),
                      ],
                      OutlinedButton(
                        onPressed: _busy ? null : _checkCompany,
                        child: const Text('بررسی مجوز شرکت در سرور حسابداری'),
                      ),
                      if (_companyLinked != null)
                        Text(
                          _companyLinked! ? 'شرکت متصل به حساب کالیا سافت' :
                            'شرکت هنوز به حساب کالیا سافت متصل نیست',
                          textAlign: TextAlign.center,
                        ),
                      const SizedBox(height: 6),
                      OutlinedButton(
                        onPressed: _busy ? null : _loadStatus,
                        child: const Text('به‌روزرسانی وضعیت اشتراک'),
                      ),
                      TextButton(
                        onPressed: _busy ? null : _logout,
                        child: const Text('خروج از حساب کالیا سافت'),
                      ),
                    ],
                    if (_busy) const Padding(
                      padding: EdgeInsets.all(12),
                      child: Center(child: CircularProgressIndicator()),
                    ),
                    if (_message != null)
                      Text(_message!, textAlign: TextAlign.center,
                        style: TextStyle(color: Theme.of(context).colorScheme.error)),
                    const SizedBox(height: 18),
                    const Text(
                      'توجه: یکپارچه‌سازی نهایی مجوز با کنترل دسترسی سمت سرور حسابداری '
                      'در حال توسعه است. این صفحه فقط حساب و وضعیت مجوز مرکزی را مدیریت می‌کند.',
                      style: TextStyle(fontSize: 12),
                      textAlign: TextAlign.center,
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
