import 'package:flutter/material.dart';

import '../../core/database/local_database.dart';
import 'audit_trail_page.dart';
import 'accounting_dimensions_page.dart';
import 'balance_sheet_page.dart';
import 'currency_accounting_home_page.dart';
import 'cached_accounts_page.dart';
import 'detail_accounts_page.dart';
import 'detail_ledger_page.dart';
import 'fiscal_years_page.dart';
import 'general_ledger_page.dart';
import 'journal_report_page.dart';
import 'local_documents_page.dart';
import 'new_journal_page.dart';
import 'profit_loss_page.dart';
import 'trial_balance_page.dart';

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
              'ثبت اسناد به‌صورت Offline-First؛ دفتر کل با ارز پایه شرکت نگهداری می‌شود.',
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
                      accessToken: accessToken,
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
                _ActionCard(
                  title: 'ابعاد حسابداری',
                  subtitle: 'مرکز هزینه و پروژه',
                  icon: Icons.hub_outlined,
                  onTap: () => _open(
                    context,
                    AccountingDimensionsPage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'حسابداری ارزی',
                  subtitle: 'ارزها، نرخ‌ها، اسناد ارزی و تسعیر',
                  icon: Icons.currency_exchange_outlined,
                  onTap: () => _open(
                    context,
                    CurrencyAccountingHomePage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'تاریخچه حسابرسی',
                  subtitle: 'ثبت، Sync، برگشت و عملیات حساس',
                  icon: Icons.manage_history_outlined,
                  onTap: () => _open(
                    context,
                    AuditTrailPage(
                      accessToken: accessToken,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'دفتر روزنامه',
                  subtitle: 'گزارش رسمی اسناد بر اساس بازه مالی',
                  icon: Icons.menu_book_outlined,
                  onTap: () => _open(
                    context,
                    JournalReportPage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'دفتر کل و معین',
                  subtitle: 'گردش و مانده حساب‌ها',
                  icon: Icons.library_books_outlined,
                  onTap: () => _open(
                    context,
                    GeneralLedgerPage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'گردش تفصیلی شناور',
                  subtitle: 'مانده مشتری، فروشنده و سایر تفصیلی‌ها',
                  icon: Icons.person_search_outlined,
                  onTap: () => _open(
                    context,
                    DetailLedgerPage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'صورت سود و زیان',
                  subtitle: 'درآمد، هزینه و سود/زیان خالص',
                  icon: Icons.show_chart_outlined,
                  onTap: () => _open(
                    context,
                    ProfitLossPage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'ترازنامه',
                  subtitle: 'دارایی، بدهی و حقوق مالکانه',
                  icon: Icons.account_balance_outlined,
                  onTap: () => _open(
                    context,
                    BalanceSheetPage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
                ),
                _ActionCard(
                  title: 'تراز آزمایشی',
                  subtitle: 'گردش بدهکار/بستانکار و کنترل تراز',
                  icon: Icons.balance_outlined,
                  onTap: () => _open(
                    context,
                    TrialBalancePage(
                      companyId: companyId,
                      accessToken: accessToken,
                      localDatabase: localDatabase,
                    ),
                  ),
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
