import 'package:flutter/material.dart';

import '../../core/database/local_database.dart';
import 'cached_accounts_page.dart';
import 'detail_accounts_page.dart';
import 'fiscal_years_page.dart';
import 'local_documents_page.dart';
import 'new_journal_page.dart';

class AccountingHomePage extends StatelessWidget {
  const AccountingHomePage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  Future<void> _open(
    BuildContext context,
    Widget page,
  ) async {
    await Navigator.of(context).push(
      MaterialPageRoute<void>(builder: (_) => page),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('حسابداری'),
        ),
        body: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            Text(
              'هسته حسابداری',
              style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                    fontWeight: FontWeight.w800,
                  ),
            ),
            const SizedBox(height: 6),
            const Text(
              'ثبت اسناد به‌صورت Offline-First؛ مبالغ پایه به ریال ذخیره می‌شوند.',
            ),
            const SizedBox(height: 24),
            Wrap(
              spacing: 16,
              runSpacing: 16,
              children: [
                _ActionCard(
                  title: 'ثبت سند جدید',
                  subtitle: 'پیش‌نویس یا آماده همگام‌سازی',
                  icon: Icons.note_add_outlined,
                  onTap: () => _open(
                    context,
                    NewJournalPage(
                      companyId: companyId,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'اسناد محلی',
                  subtitle: 'Draft و Pending Sync',
                  icon: Icons.receipt_long_outlined,
                  onTap: () => _open(
                    context,
                    LocalDocumentsPage(
                      companyId: companyId,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'کدینگ حساب‌ها',
                  subtitle: 'خواندن مستقیم از SQLite محلی',
                  icon: Icons.account_tree_outlined,
                  onTap: () => _open(
                    context,
                    CachedAccountsPage(
                      companyId: companyId,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'سال‌های مالی',
                  subtitle: 'ایجاد و مشاهده سال مالی شمسی',
                  icon: Icons.calendar_month_outlined,
                  onTap: () => _open(
                    context,
                    FiscalYearsPage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'تفصیلی‌های شناور',
                  subtitle: 'مشتری، فروشنده، شخص، بانک و ...',
                  icon: Icons.badge_outlined,
                  onTap: () => _open(
                    context,
                    DetailAccountsPage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                const _ActionCard(
                  title: 'دفتر روزنامه',
                  subtitle: 'مرحله بعد',
                  icon: Icons.menu_book_outlined,
                ),
                const _ActionCard(
                  title: 'دفتر کل و معین',
                  subtitle: 'مرحله بعد',
                  icon: Icons.library_books_outlined,
                ),
                const _ActionCard(
                  title: 'تراز آزمایشی',
                  subtitle: 'مرحله بعد',
                  icon: Icons.balance_outlined,
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _ActionCard extends StatelessWidget {
  const _ActionCard({
    required this.title,
    required this.subtitle,
    required this.icon,
    this.onTap,
  });

  final String title;
  final String subtitle;
  final IconData icon;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 280,
      child: Card(
        child: InkWell(
          borderRadius: BorderRadius.circular(12),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Row(
              children: [
                CircleAvatar(
                  child: Icon(icon),
                ),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: Theme.of(context).textTheme.titleMedium?.copyWith(
                              fontWeight: FontWeight.w700,
                            ),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        subtitle,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                Icon(
                  onTap == null
                      ? Icons.lock_clock_outlined
                      : Icons.chevron_left,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
