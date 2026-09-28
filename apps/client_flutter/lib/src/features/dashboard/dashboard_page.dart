import 'package:flutter/material.dart';

class DashboardPage extends StatelessWidget {
  const DashboardPage({super.key});

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
                child: SafeArea(child: _Navigation(items: _items)),
              )
            : null,
        body: Row(
          children: [
            if (!compact)
              SizedBox(
                width: 250,
                child: _Navigation(items: _items),
              ),
            const Expanded(child: _DashboardBody()),
          ],
        ),
      ),
    );
  }
}

class _Navigation extends StatelessWidget {
  const _Navigation({required this.items});

  final List<_NavItem> items;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Theme.of(context).colorScheme.surface,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          const ListTile(
            leading: CircleAvatar(child: Icon(Icons.business)),
            title: Text('شرکت نمونه'),
            subtitle: Text('سال مالی جاری'),
          ),
          const Divider(height: 32),
          for (final item in items)
            ListTile(
              leading: Icon(item.icon),
              title: Text(item.label),
              selected: item == items.first,
              onTap: () {},
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
  const _DashboardBody();

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
                child: Text(
                  'داشبورد مدیریتی',
                  style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                        fontWeight: FontWeight.w800,
                      ),
                ),
              ),
              const Chip(
                avatar: Icon(Icons.cloud_done_outlined, size: 18),
                label: Text('Sync آماده'),
              ),
            ],
          ),
          const SizedBox(height: 24),
          const Wrap(
            spacing: 16,
            runSpacing: 16,
            children: [
              _MetricCard('فروش امروز', '—', Icons.trending_up),
              _MetricCard('دریافتنی‌ها', '—', Icons.payments_outlined),
              _MetricCard('موجودی انبار', '—', Icons.inventory_outlined),
              _MetricCard('مانده نقد', '—', Icons.account_balance_wallet_outlined),
            ],
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
                  const _StatusRow('هسته API', true),
                  const _StatusRow('Company / User / Device models', true),
                  const _StatusRow('PostgreSQL + SQLite persistence', false),
                  const _StatusRow('TOTP / QR Pairing', false),
                  const _StatusRow('Sync Engine', false),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
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
      leading: Icon(done ? Icons.check_circle : Icons.radio_button_unchecked),
      title: Text(label),
    );
  }
}

class _NavItem {
  const _NavItem(this.label, this.icon);

  final String label;
  final IconData icon;
}
