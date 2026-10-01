import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';

class SalesInventorySettingsPage extends StatefulWidget {
  const SalesInventorySettingsPage({
    super.key,
    required this.accessToken,
    required this.companyId,
    required this.localDatabase,
  });

  final String accessToken;
  final String companyId;
  final LocalDatabase localDatabase;

  @override
  State<SalesInventorySettingsPage> createState() =>
      _SalesInventorySettingsPageState();
}

class _SalesInventorySettingsPageState
    extends State<SalesInventorySettingsPage> {
  final _apiClient = ApiClient();

  List<CachedAccount> _accounts = const [];
  Map<String, dynamic>? _settings;
  bool _loading = true;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final results = await Future.wait([
        widget.localDatabase
            .getCachedAccounts(widget.companyId),
        _apiClient.getSalesInventorySettings(
          bearerToken: widget.accessToken,
        ),
      ]);

      if (!mounted) return;

      setState(() {
        _accounts = (results[0] as List<CachedAccount>)
            .where(
              (account) =>
                  account.isActive &&
                  account.isPostable,
            )
            .toList(growable: false);
        _settings = Map<String, dynamic>.from(
          results[1] as Map,
        );
      });
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  List<CachedAccount> _ofType(String type) {
    return _accounts
        .where((account) => account.type == type)
        .toList(growable: false);
  }

  Future<void> _save() async {
    final settings = _settings;
    if (settings == null) return;

    setState(() => _saving = true);

    try {
      final updated =
          await _apiClient.updateSalesInventorySettings(
        bearerToken: widget.accessToken,
        settings: settings,
      );

      if (!mounted) return;
      setState(() => _settings = updated);

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'اتصال حسابداری فروش و انبار ذخیره شد.',
          ),
        ),
      );
    } on ApiException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(error.message)),
      );
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Widget _accountField({
    required String keyName,
    required String label,
    required String type,
  }) {
    final settings = _settings!;
    final accounts = _ofType(type);
    final current = settings[keyName]?.toString();

    return DropdownButtonFormField<String>(
      initialValue: accounts.any((x) => x.id == current)
          ? current
          : null,
      isExpanded: true,
      decoration: InputDecoration(
        labelText: label,
      ),
      items: [
        for (final account in accounts)
          DropdownMenuItem(
            value: account.id,
            child: Text(
              account.code + ' — ' + account.name,
            ),
          ),
      ],
      onChanged: _saving
          ? null
          : (value) {
              if (value == null) return;
              setState(
                () => settings[keyName] = value,
              );
            },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text(
            'اتصال فروش و انبار به حسابداری',
          ),
        ),
        body: _loading
            ? const Center(
                child: CircularProgressIndicator(),
              )
            : _error != null
                ? Center(child: Text(_error!))
                : ListView(
                    padding: const EdgeInsets.all(20),
                    children: [
                      Card(
                        child: Padding(
                          padding:
                              const EdgeInsets.all(18),
                          child: Column(
                            crossAxisAlignment:
                                CrossAxisAlignment.stretch,
                            children: [
                              const Text(
                                'نگاشت حساب‌ها',
                                style: TextStyle(
                                  fontSize: 18,
                                  fontWeight:
                                      FontWeight.w700,
                                ),
                              ),
                              const SizedBox(height: 8),
                              const Text(
                                'فروش و خرید قطعی با این نگاشت‌ها به‌صورت خودکار سند حسابداری می‌سازند.',
                              ),
                              const SizedBox(height: 18),
                              _accountField(
                                keyName:
                                    'receivablesAccountId',
                                label:
                                    'حساب‌های دریافتنی',
                                type: 'Asset',
                              ),
                              const SizedBox(height: 12),
                              _accountField(
                                keyName:
                                    'payablesAccountId',
                                label:
                                    'حساب‌های پرداختنی خرید',
                                type: 'Liability',
                              ),
                              const SizedBox(height: 12),
                              _accountField(
                                keyName: 'cashAccountId',
                                label: 'صندوق / فروش نقدی',
                                type: 'Asset',
                              ),
                              const SizedBox(height: 12),
                              _accountField(
                                keyName:
                                    'salesRevenueAccountId',
                                label: 'درآمد فروش',
                                type: 'Revenue',
                              ),
                              const SizedBox(height: 12),
                              _accountField(
                                keyName:
                                    'inventoryAccountId',
                                label: 'موجودی کالا',
                                type: 'Asset',
                              ),
                              const SizedBox(height: 12),
                              _accountField(
                                keyName:
                                    'costOfGoodsSoldAccountId',
                                label: 'بهای تمام‌شده فروش',
                                type: 'Expense',
                              ),
                              const SizedBox(height: 12),
                              _accountField(
                                keyName:
                                    'salesTaxPayableAccountId',
                                label:
                                    'مالیات و عوارض فروش پرداختنی',
                                type: 'Liability',
                              ),
                              const SizedBox(height: 12),
                              _accountField(
                                keyName:
                                    'purchaseTaxReceivableAccountId',
                                label:
                                    'مالیات و عوارض خرید قابل‌دریافت',
                                type: 'Asset',
                              ),
                              const SizedBox(height: 12),
                              SwitchListTile(
                                contentPadding:
                                    EdgeInsets.zero,
                                title: const Text(
                                  'جلوگیری از موجودی منفی',
                                ),
                                value: _settings![
                                            'preventNegativeStock']
                                        as bool? ??
                                    true,
                                onChanged: _saving
                                    ? null
                                    : (value) {
                                        setState(
                                          () => _settings![
                                                  'preventNegativeStock'] =
                                              value,
                                        );
                                      },
                              ),
                              const SizedBox(height: 12),
                              FilledButton.icon(
                                onPressed:
                                    _saving ? null : _save,
                                icon: _saving
                                    ? const SizedBox(
                                        width: 18,
                                        height: 18,
                                        child:
                                            CircularProgressIndicator(
                                          strokeWidth: 2,
                                        ),
                                      )
                                    : const Icon(
                                        Icons.save_outlined,
                                      ),
                                label: const Text(
                                  'ذخیره تنظیمات',
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ],
                  ),
      ),
    );
  }
}
