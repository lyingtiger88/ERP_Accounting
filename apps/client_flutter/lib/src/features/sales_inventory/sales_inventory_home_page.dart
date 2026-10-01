import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../accounting/report_support.dart';
import 'products_page.dart';
import 'sales_invoices_page.dart';
import 'sales_inventory_settings_page.dart';
import 'stock_page.dart';
import 'warehouses_page.dart';

class SalesInventoryHomePage extends StatefulWidget {
  const SalesInventoryHomePage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<SalesInventoryHomePage> createState() =>
      _SalesInventoryHomePageState();
}

class _SalesInventoryHomePageState
    extends State<SalesInventoryHomePage> {
  final _apiClient = ApiClient();

  Future<_StoreOverview>? _future;

  @override
  void initState() {
    super.initState();
    _future = _loadOverview();
  }

  Future<_StoreOverview> _loadOverview() async {
    await _apiClient.ensureSalesInventoryDefaults(
      bearerToken: widget.accessToken,
    );

    final results = await Future.wait([
      _apiClient.getStoreProducts(
        bearerToken: widget.accessToken,
      ),
      _apiClient.getWarehouses(
        bearerToken: widget.accessToken,
      ),
      _apiClient.getStockBalances(
        bearerToken: widget.accessToken,
      ),
      _apiClient.getSalesInvoices(
        bearerToken: widget.accessToken,
      ),
    ]);

    final products =
        results[0] as List<Map<String, dynamic>>;
    final warehouses =
        results[1] as List<Map<String, dynamic>>;
    final stock =
        results[2] as List<Map<String, dynamic>>;
    final invoices =
        results[3] as List<Map<String, dynamic>>;

    final inventoryValue = stock.fold<num>(
      0,
      (sum, row) =>
          sum + reportNumber(row['inventoryValue']),
    );

    final postedSales = invoices
        .where(
          (invoice) =>
              invoice['status'].toString() == 'Posted',
        )
        .fold<num>(
          0,
          (sum, invoice) =>
              sum + reportNumber(invoice['grandTotal']),
        );

    final draftInvoices = invoices
        .where(
          (invoice) =>
              invoice['status'].toString() == 'Draft',
        )
        .length;

    return _StoreOverview(
      productCount: products
          .where(
            (product) =>
                product['isActive'] as bool? ?? true,
          )
          .length,
      warehouseCount: warehouses
          .where(
            (warehouse) =>
                warehouse['isActive'] as bool? ?? true,
          )
          .length,
      inventoryValue: inventoryValue,
      invoiceCount: invoices.length,
      draftInvoices: draftInvoices,
      postedSales: postedSales,
    );
  }

  Future<void> _refresh() async {
    setState(() {
      _future = _loadOverview();
    });

    await _future;
  }

  Future<void> _open(Widget page) async {
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => page,
      ),
    );

    if (!mounted) return;
    await _refresh();
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('فروشگاه و انبار'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _refresh,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: FutureBuilder<_StoreOverview>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState !=
                ConnectionState.done) {
              return const Center(
                child: CircularProgressIndicator(),
              );
            }

            if (snapshot.hasError) {
              return Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(
                    'راه‌اندازی ماژول فروش و انبار ناموفق بود:\n' +
                        snapshot.error.toString(),
                    textAlign: TextAlign.center,
                  ),
                ),
              );
            }

            final overview = snapshot.data!;

            return ListView(
              padding: const EdgeInsets.all(24),
              children: [
                Text(
                  'مدیریت فروشگاه و انبار',
                  style: Theme.of(context)
                      .textTheme
                      .headlineSmall
                      ?.copyWith(
                        fontWeight: FontWeight.w800,
                      ),
                ),
                const SizedBox(height: 6),
                const Text(
                  'این ماژول مستقل از حسابداری قابل استفاده است؛ ثبت قطعی فروش می‌تواند به‌صورت خودکار سند حسابداری و بهای تمام‌شده ایجاد کند.',
                ),
                const SizedBox(height: 20),
                Wrap(
                  spacing: 14,
                  runSpacing: 14,
                  children: [
                    _MetricCard(
                      title: 'کالا / خدمت فعال',
                      value:
                          overview.productCount.toString(),
                      icon: Icons.inventory_2_outlined,
                    ),
                    _MetricCard(
                      title: 'انبار فعال',
                      value:
                          overview.warehouseCount.toString(),
                      icon: Icons.warehouse_outlined,
                    ),
                    _MetricCard(
                      title: 'ارزش موجودی',
                      value: formatReportMoney(
                            overview.inventoryValue,
                          ) +
                          ' ریال',
                      icon:
                          Icons.monetization_on_outlined,
                    ),
                    _MetricCard(
                      title: 'فاکتور پیش‌نویس',
                      value:
                          overview.draftInvoices.toString(),
                      icon: Icons.edit_note_outlined,
                    ),
                    _MetricCard(
                      title: 'فروش قطعی',
                      value: formatReportMoney(
                            overview.postedSales,
                          ) +
                          ' ریال',
                      icon: Icons.point_of_sale_outlined,
                    ),
                  ],
                ),
                const SizedBox(height: 24),
                Wrap(
                  spacing: 14,
                  runSpacing: 14,
                  children: [
                    _ModuleCard(
                      title: 'فاکتورهای فروش',
                      subtitle:
                          'صدور، پیش‌نویس و ثبت قطعی فروش',
                      icon: Icons.receipt_long_outlined,
                      onTap: () => _open(
                        SalesInvoicesPage(
                          companyId: widget.companyId,
                          accessToken: widget.accessToken,
                          localDatabase:
                              widget.localDatabase,
                        ),
                      ),
                    ),
                    _ModuleCard(
                      title: 'کالاها و خدمات',
                      subtitle:
                          'کد کالا، بارکد، قیمت و نوع موجودی',
                      icon: Icons.category_outlined,
                      onTap: () => _open(
                        ProductsPage(
                          accessToken:
                              widget.accessToken,
                        ),
                      ),
                    ),
                    _ModuleCard(
                      title: 'موجودی انبار',
                      subtitle:
                          'موجودی، میانگین بها و تعدیل انبار',
                      icon: Icons.inventory_outlined,
                      onTap: () => _open(
                        StockPage(
                          accessToken:
                              widget.accessToken,
                        ),
                      ),
                    ),
                    _ModuleCard(
                      title: 'انبارها',
                      subtitle:
                          'تعریف و مدیریت انبارهای فروشگاه',
                      icon: Icons.warehouse_outlined,
                      onTap: () => _open(
                        WarehousesPage(
                          accessToken:
                              widget.accessToken,
                        ),
                      ),
                    ),
                    _ModuleCard(
                      title: 'اتصال به حسابداری',
                      subtitle:
                          'نگاشت فروش، موجودی، بهای تمام‌شده و مالیات',
                      icon: Icons.link_outlined,
                      onTap: () => _open(
                        SalesInventorySettingsPage(
                          companyId: widget.companyId,
                          accessToken: widget.accessToken,
                          localDatabase:
                              widget.localDatabase,
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
                      children: [
                        const Text(
                          'ارتباط با حسابداری',
                          style: TextStyle(
                            fontSize: 18,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        const SizedBox(height: 12),
                        const _IntegrationRow(
                          'فروش نقدی → صندوق / بانک',
                        ),
                        const _IntegrationRow(
                          'فروش نسیه → حساب دریافتنی + تفصیلی مشتری',
                        ),
                        const _IntegrationRow(
                          'فروش کالا → درآمد فروش + مالیات پرداختنی',
                        ),
                        const _IntegrationRow(
                          'خروج کالا → بهای تمام‌شده + کاهش موجودی',
                        ),
                        const _IntegrationRow(
                          'ثبت قطعی → سند حسابداری لینک‌شده',
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

class _StoreOverview {
  const _StoreOverview({
    required this.productCount,
    required this.warehouseCount,
    required this.inventoryValue,
    required this.invoiceCount,
    required this.draftInvoices,
    required this.postedSales,
  });

  final int productCount;
  final int warehouseCount;
  final num inventoryValue;
  final int invoiceCount;
  final int draftInvoices;
  final num postedSales;
}

class _MetricCard extends StatelessWidget {
  const _MetricCard({
    required this.title,
    required this.value,
    required this.icon,
  });

  final String title;
  final String value;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 220,
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment:
                CrossAxisAlignment.start,
            children: [
              Icon(icon),
              const SizedBox(height: 14),
              Text(title),
              const SizedBox(height: 6),
              Text(
                value,
                style: Theme.of(context)
                    .textTheme
                    .titleLarge
                    ?.copyWith(
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

class _ModuleCard extends StatelessWidget {
  const _ModuleCard({
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
      width: 300,
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
                        style:
                            Theme.of(context)
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

class _IntegrationRow extends StatelessWidget {
  const _IntegrationRow(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      contentPadding: EdgeInsets.zero,
      leading: const Icon(
        Icons.check_circle_outline,
      ),
      title: Text(text),
    );
  }
}
