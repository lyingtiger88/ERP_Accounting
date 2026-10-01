import 'package:flutter/material.dart';

import '../../core/database/local_database.dart';
import '../accounting/accounting_home_page.dart';
import '../sales_inventory/sales_inventory_home_page.dart';

class DashboardPage extends StatelessWidget {
  const DashboardPage({
    super.key,
    required this.displayName,
    required this.role,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
    required this.accountsSynced,
  });

  final String displayName;
  final String role;
  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;
  final bool accountsSynced;

  static const _items = <_NavItem>[
    _NavItem('داشبورد', Icons.dashboard_outlined),
    _NavItem('حسابداری', Icons.account_balance_outlined),
    _NavItem('فروش', Icons.point_of_sale_outlined),
    _NavItem('خرید', Icons.shopping_cart_outlined),
    _NavItem('انبار', Icons.inventory_2_outlined),
    _NavItem('بانک و صندوق', Icons.account_balance_wallet_outlined),
    _NavItem('منابع', Icons.hub_outlined),
    _NavItem('گزارش‌ها', Icons.bar_chart_outlined),
    _NavItem('تنظیمات', Icons.settings_outlined),
  ];

  @override
  Widget build(BuildContext context) {
    final compact = MediaQuery.sizeOf(context).width < 760;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: compact
            ? AppBar(
                title: const Text('ERP Accounting'),
              )
            : null,
        drawer: compact
            ? Drawer(
                child: SafeArea(
                  child: _Navigation(
                    items: _items,
                    displayName: displayName,
                    role: role,
                    companyId: companyId,
                    accessToken: accessToken,
                    localDatabase: localDatabase,
                  ),
                ),
              )
            : null,
        body: Row(
          children: [
            if (!compact)
              SizedBox(
                width: 250,
                child: _Navigation(
                  items: _items,
                  displayName: displayName,
                  role: role,
                  companyId: companyId,
                  accessToken: accessToken,
                  localDatabase: localDatabase,
                ),
              ),
            Expanded(
              child: _DashboardBody(
                displayName: displayName,
                companyId: companyId,
                localDatabase: localDatabase,
                accountsSynced: accountsSynced,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Navigation extends StatelessWidget {
  const _Navigation({
    required this.items,
    required this.displayName,
    required this.role,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final List<_NavItem> items;
  final String displayName;
  final String role;
  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Theme.of(context).colorScheme.surface,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          ListTile(
            leading: const CircleAvatar(child: Icon(Icons.person_outline)),
            title: Text(displayName),
            subtitle: Text(role),
          ),
          const Divider(height: 32),
          for (final item in items)
            ListTile(
              leading: Icon(item.icon),
              title: Text(item.label),
              selected: item == items.first,
              onTap: () {
                if (item.label == 'حسابداری') {
                  Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => AccountingHomePage(
                        companyId: companyId,
                        accessToken: accessToken,
                        localDatabase: localDatabase,
                      ),
                    ),
                  );
                  return;
                }

                if (item.label == 'فروش' ||
                    item.label == 'انبار') {
                  Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => SalesInventoryHomePage(
                        companyId: companyId,
                        accessToken: accessToken,
                        localDatabase: localDatabase,
                      ),
                    ),
                  );
                }
              },
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(12),
              ),
            ),
        ],
      ),
    );
  }
}

class _DashboardBody extends StatelessWidget {
  const _DashboardBody({
    required this.displayName,
    required this.companyId,
    required this.localDatabase,
    required this.accountsSynced,
  });

  final String displayName;
  final String companyId;
  final LocalDatabase localDatabase;
  final bool accountsSynced;

  Future<_LocalStatus> _loadLocalStatus() async {
    return _LocalStatus(
      cachedAccounts:
          await localDatabase.cachedAccountCount(companyId),
      pendingChanges:
          await localDatabase.pendingOutboxCount(),
      unresolvedConflicts:
          await localDatabase.unresolvedConflictCount(companyId),
      databasePath: localDatabase.databasePath ?? 'unknown',
    );
  }

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(28),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'داشبورد مدیریتی',
                      style:
                          Theme.of(context).textTheme.headlineMedium?.copyWith(
                                fontWeight: FontWeight.w800,
                              ),
                    ),
                    const SizedBox(height: 6),
                    Text('خوش آمدی، ' + displayName),
                  ],
                ),
              ),
              Chip(
                avatar: Icon(
                  accountsSynced
                      ? Icons.cloud_done_outlined
                      : Icons.cloud_off_outlined,
                  size: 18,
                ),
                label: Text(
                  accountsSynced
                      ? 'Online + Local Cache'
                      : 'Local Cache',
                ),
              ),
            ],
          ),
          const SizedBox(height: 24),
          FutureBuilder<_LocalStatus>(
            future: _loadLocalStatus(),
            builder: (context, snapshot) {
              final status = snapshot.data;

              return Wrap(
                spacing: 16,
                runSpacing: 16,
                children: [
                  const _MetricCard(
                    'فروش امروز',
                    '—',
                    Icons.trending_up,
                  ),
                  const _MetricCard(
                    'دریافتنی‌ها',
                    '—',
                    Icons.payments_outlined,
                  ),
                  _MetricCard(
                    'حساب‌های کش‌شده',
                    status?.cachedAccounts.toString() ?? '…',
                    Icons.storage_outlined,
                  ),
                  _MetricCard(
                    'تغییرات منتظر Sync',
                    status?.pendingChanges.toString() ?? '…',
                    Icons.sync_outlined,
                  ),
                  _MetricCard(
                    'تعارض‌های حل‌نشده',
                    status?.unresolvedConflicts.toString() ?? '…',
                    Icons.sync_problem_outlined,
                  ),
                ],
              );
            },
          ),
          const SizedBox(height: 24),
          FutureBuilder<_LocalStatus>(
            future: _loadLocalStatus(),
            builder: (context, snapshot) {
              final path = snapshot.data?.databasePath ?? 'در حال بارگذاری...';

              return Card(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        'Offline-First Storage',
                        style:
                            Theme.of(context).textTheme.titleLarge?.copyWith(
                                  fontWeight: FontWeight.w700,
                                ),
                      ),
                      const SizedBox(height: 12),
                      const Text(
                        'SQLite محلی روی همین دستگاه فعال است.',
                      ),
                      const SizedBox(height: 8),
                      SelectableText(
                        path,
                        textDirection: TextDirection.ltr,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
          const SizedBox(height: 24),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'وضعیت Phase 1',
                    style: Theme.of(context).textTheme.titleLarge?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                  ),
                  const SizedBox(height: 16),
                  const _StatusRow('رابط Windows / Android', true),
                  const _StatusRow('اتصال Login به API', true),
                  const _StatusRow('Company / User / Device models', true),
                  const _StatusRow('کدینگ اولیه حساب‌ها', true),
                  const _StatusRow('اعتبارسنجی سند دوطرفه', true),
                  const _StatusRow('Server persistence', true),
                  const _StatusRow('Client SQLite / Offline cache', true),
                  const _StatusRow('Outbox foundation', true),
                  const _StatusRow('Journal Outbox Push', true),
                  const _StatusRow('Idempotent journal sync', true),
                  const _StatusRow('Journal Pull / Sync Cursor', true),
                  const _StatusRow('Offline journal entry', true),
                  const _StatusRow('Fiscal years / Jalali dates', true),
                  const _StatusRow('Fiscal periods / close control', true),
                  const _StatusRow('Journal reversal / audit trail', true),
                  const _StatusRow('Core accounting reports', true),
                  const _StatusRow('Floating detail accounts', true),
                  const _StatusRow('Detail entity revisions', true),
                  const _StatusRow('Conflict detection / resolution', true),
                  const _StatusRow('Master-data delta cursor', true),
                  const _StatusRow('TOTP / QR Pairing', false),
                  const _StatusRow('Full Sync Engine', false),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _LocalStatus {
  const _LocalStatus({
    required this.cachedAccounts,
    required this.pendingChanges,
    required this.unresolvedConflicts,
    required this.databasePath,
  });

  final int cachedAccounts;
  final int pendingChanges;
  final int unresolvedConflicts;
  final String databasePath;
}

class _MetricCard extends StatelessWidget {
  const _MetricCard(this.title, this.value, this.icon);

  final String title;
  final String value;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 230,
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon),
              const SizedBox(height: 20),
              Text(title),
              const SizedBox(height: 8),
              Text(
                value,
                style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _StatusRow extends StatelessWidget {
  const _StatusRow(this.label, this.done);

  final String label;
  final bool done;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      contentPadding: EdgeInsets.zero,
      leading: Icon(
        done ? Icons.check_circle : Icons.radio_button_unchecked,
      ),
      title: Text(label),
    );
  }
}

class _NavItem {
  const _NavItem(this.label, this.icon);

  final String label;
  final IconData icon;
}
