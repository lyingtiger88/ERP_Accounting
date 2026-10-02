import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import 'currencies_page.dart';
import 'currency_position_page.dart';
import 'currency_rates_page.dart';
import 'foreign_currency_journal_page.dart';

class CurrencyAccountingHomePage extends StatefulWidget {
  const CurrencyAccountingHomePage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<CurrencyAccountingHomePage> createState() =>
      _CurrencyAccountingHomePageState();
}

class _CurrencyAccountingHomePageState
    extends State<CurrencyAccountingHomePage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  void _reload() {
    _future = _apiClient.getCurrencies(
      bearerToken: widget.accessToken,
    );
  }

  Future<void> _open(Widget page) async {
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => page,
      ),
    );

    if (!mounted) return;
    setState(_reload);
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('حسابداری ارزی'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
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
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(snapshot.error.toString()),
                ),
              );
            }

            final currencies =
                snapshot.data ?? const <Map<String, dynamic>>[];

            Map<String, dynamic>? base;
            for (final item in currencies) {
              if (item['isBase'] as bool? ?? false) {
                base = item;
                break;
              }
            }

            final activeForeign = currencies
                .where(
                  (x) =>
                      (x['isActive'] as bool? ?? true) &&
                      !(x['isBase'] as bool? ?? false),
                )
                .length;

            return ListView(
              padding: const EdgeInsets.all(24),
              children: [
                Text(
                  'حسابداری چندارزی',
                  style: Theme.of(context)
                      .textTheme
                      .headlineSmall
                      ?.copyWith(
                        fontWeight: FontWeight.w800,
                      ),
                ),
                const SizedBox(height: 6),
                const Text(
                  'مدیریت ارزهای بین‌المللی، نرخ‌های تاریخی، اسناد ارزی و موقعیت تسعیر بدون تغییر مبالغ دفتر کل در ارز پایه.',
                ),
                const SizedBox(height: 18),
                Wrap(
                  spacing: 12,
                  runSpacing: 12,
                  children: [
                    _InfoChip(
                      icon: Icons.home_outlined,
                      label: base == null
                          ? 'ارز پایه تعریف نشده'
                          : 'ارز پایه: ' +
                              base['code'].toString(),
                    ),
                    _InfoChip(
                      icon: Icons.public_outlined,
                      label: 'ارز خارجی فعال: ' +
                          activeForeign.toString(),
                    ),
                    const _InfoChip(
                      icon: Icons.swap_horiz_outlined,
                      label:
                          'نرخ: خرید / فروش / حسابداری',
                    ),
                  ],
                ),
                const SizedBox(height: 24),
                Wrap(
                  spacing: 14,
                  runSpacing: 14,
                  children: [
                    _CurrencyActionCard(
                      title: 'تعریف ارزها',
                      subtitle:
                          'ISO Code، نماد، اعشار و ارز پایه شرکت',
                      icon: Icons.language_outlined,
                      onTap: () => _open(
                        CurrenciesPage(
                          accessToken: widget.accessToken,
                        ),
                      ),
                    ),
                    _CurrencyActionCard(
                      title: 'نرخ‌گذاری ارز',
                      subtitle:
                          'تاریخچه نرخ خرید، فروش و نرخ حسابداری',
                      icon: Icons.currency_exchange_outlined,
                      onTap: () => _open(
                        CurrencyRatesPage(
                          accessToken: widget.accessToken,
                        ),
                      ),
                    ),
                    _CurrencyActionCard(
                      title: 'ثبت سند ارزی',
                      subtitle:
                          'مبلغ اصلی ارز + نرخ + معادل ارز پایه',
                      icon: Icons.receipt_long_outlined,
                      onTap: () => _open(
                        ForeignCurrencyJournalPage(
                          companyId: widget.companyId,
                          accessToken: widget.accessToken,
                          localDatabase: widget.localDatabase,
                        ),
                      ),
                    ),
                    _CurrencyActionCard(
                      title: 'موقعیت و تسعیر ارزی',
                      subtitle:
                          'مانده ارزی حساب‌های دارایی/بدهی و تفاوت نرخ',
                      icon: Icons.assessment_outlined,
                      onTap: () => _open(
                        CurrencyPositionPage(
                          accessToken: widget.accessToken,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 24),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      crossAxisAlignment:
                          CrossAxisAlignment.stretch,
                      children: const [
                        Text(
                          'منطق ثبت چندارزی',
                          style: TextStyle(
                            fontSize: 18,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        SizedBox(height: 10),
                        _RuleRow(
                          'دفتر کل و صورت‌های مالی با ارز پایه شرکت نگهداری می‌شوند.',
                        ),
                        _RuleRow(
                          'مبلغ اصلی ارز و نرخ استفاده‌شده روی هر خط سند جدا ذخیره می‌شود.',
                        ),
                        _RuleRow(
                          'برای هر تاریخ می‌توان نرخ خرید، فروش و نرخ حسابداری مستقل ثبت کرد.',
                        ),
                        _RuleRow(
                          'Reversal سند، مبلغ ارز و نرخ تاریخی همان سند را نیز حفظ می‌کند.',
                        ),
                        _RuleRow(
                          'گزارش تسعیر، مانده ارزی حساب‌های دارایی و بدهی را با نرخ روز مقایسه می‌کند.',
                        ),
                      ],
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

class _CurrencyActionCard extends StatelessWidget {
  const _CurrencyActionCard({
    required this.title,
    required this.subtitle,
    required this.icon,
    required this.onTap,
  });

  final String title;
  final String subtitle;
  final IconData icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 310,
      child: Card(
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(12),
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Row(
              children: [
                CircleAvatar(child: Icon(icon)),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment:
                        CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: const TextStyle(
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        subtitle,
                        style: Theme.of(context)
                            .textTheme
                            .bodySmall,
                      ),
                    ],
                  ),
                ),
                const Icon(Icons.chevron_left),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _InfoChip extends StatelessWidget {
  const _InfoChip({
    required this.icon,
    required this.label,
  });

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Chip(
      avatar: Icon(icon, size: 17),
      label: Text(label),
    );
  }
}

class _RuleRow extends StatelessWidget {
  const _RuleRow(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      dense: true,
      contentPadding: EdgeInsets.zero,
      leading: const Icon(
        Icons.check_circle_outline,
        size: 20,
      ),
      title: Text(text),
    );
  }
}
