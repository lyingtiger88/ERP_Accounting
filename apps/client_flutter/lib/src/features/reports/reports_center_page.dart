import 'dart:io';

import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/demo/demo_mode.dart';
import '../../core/demo/local_demo_reporting_engine.dart';
import '../accounting/balance_sheet_page.dart';
import '../accounting/detail_ledger_page.dart';
import '../accounting/general_ledger_page.dart';
import '../accounting/journal_report_page.dart';
import '../accounting/profit_loss_page.dart';
import '../accounting/report_support.dart';
import '../accounting/trial_balance_page.dart';

class ReportsCenterPage extends StatefulWidget {
  const ReportsCenterPage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<ReportsCenterPage> createState() =>
      _ReportsCenterPageState();
}

class _ReportsCenterPageState extends State<ReportsCenterPage> {
  final _api = ApiClient();

  AccountingReportPeriod? _period;
  Future<Map<String, dynamic>>? _future;
  List<Map<String, dynamic>> _products = const [];
  List<Map<String, dynamic>> _warehouses = const [];
  List<CachedDetailAccount> _details = const [];
  String? _warehouseId;
  String? _productId;
  String? _detailId;
  bool _loading = true;
  bool _exporting = false;

  @override
  void initState() {
    super.initState();
    _bootstrap();
  }

  Future<void> _bootstrap() async {
    final period = await AccountingReportPeriod.load(
      localDatabase: widget.localDatabase,
      companyId: widget.companyId,
    );

    var products = await widget.localDatabase.getCachedStoreEntities(
      companyId: widget.companyId,
      entityType: 'StoreProduct',
    );
    var warehouses =
        await widget.localDatabase.getCachedStoreEntities(
      companyId: widget.companyId,
      entityType: 'Warehouse',
    );
    var details = await widget.localDatabase.getCachedDetailAccounts(
      widget.companyId,
    );

    if (!DemoMode.isDemoToken(widget.accessToken)) {
      try {
        final remoteProducts = await _api.getStoreProducts(
          bearerToken: widget.accessToken,
        );
        final remoteWarehouses = await _api.getWarehouses(
          bearerToken: widget.accessToken,
        );
        final remoteDetails = await _api.getDetailAccounts(
          bearerToken: widget.accessToken,
        );

        products = remoteProducts;
        warehouses = remoteWarehouses;

        await widget.localDatabase.replaceStoreEntities(
          companyId: widget.companyId,
          entityType: 'StoreProduct',
          items: products,
        );
        await widget.localDatabase.replaceStoreEntities(
          companyId: widget.companyId,
          entityType: 'Warehouse',
          items: warehouses,
        );
        await widget.localDatabase.replaceDetailAccounts(
          companyId: widget.companyId,
          details: remoteDetails,
        );
        details = await widget.localDatabase.getCachedDetailAccounts(
          widget.companyId,
        );
      } on ApiException {
        // Cached master data keeps reporting usable during connection loss.
      }
    }

    if (!mounted) return;

    setState(() {
      _period = period;
      _products = products;
      _warehouses = warehouses;
      _details = details;
      _loading = false;
      _future = _fetch();
    });
  }

  Future<Map<String, dynamic>> _fetch() {
    final period = _period;

    if (DemoMode.isDemoToken(widget.accessToken)) {
      return LocalDemoReportingEngine(
        localDatabase: widget.localDatabase,
      ).getReportsCenter(
        companyId: widget.companyId,
        from: period?.from,
        to: period?.to,
        warehouseId: _warehouseId,
        productId: _productId,
        detailAccountId: _detailId,
      );
    }

    return _api.getReportsCenter(
      bearerToken: widget.accessToken,
      from: period?.from,
      to: period?.to,
      warehouseId: _warehouseId,
      productId: _productId,
      detailAccountId: _detailId,
    );
  }

  void _refresh() {
    setState(() {
      _future = _fetch();
    });
  }

  Future<void> _pickFrom() async {
    final period = _period;
    if (period == null) return;

    final picked = await showDatePicker(
      context: context,
      initialDate: period.from ?? DateTime.now(),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (picked == null || !mounted) return;

    if (period.to != null && picked.isAfter(period.to!)) {
      _message('تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.');
      return;
    }

    setState(() {
      _period = period.copyWith(from: picked);
      _future = _fetch();
    });
  }

  Future<void> _pickTo() async {
    final period = _period;
    if (period == null) return;

    final picked = await showDatePicker(
      context: context,
      initialDate: period.to ?? DateTime.now(),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (picked == null || !mounted) return;

    if (period.from != null && picked.isBefore(period.from!)) {
      _message('تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد.');
      return;
    }

    setState(() {
      _period = period.copyWith(to: picked);
      _future = _fetch();
    });
  }

  void _selectFiscalYear(String? id) {
    final period = _period;
    if (period == null) return;

    setState(() {
      _period = period.selectFiscalYear(id);
      _future = _fetch();
    });
  }

  Future<void> _exportCsv(Map<String, dynamic> report) async {
    if (_exporting) return;
    setState(() => _exporting = true);

    try {
      final buffer = StringBuffer();

      void section(
        String title,
        List<String> headers,
        List<dynamic> rows,
        List<dynamic> Function(Map<String, dynamic>) values,
      ) {
        buffer.writeln(title);
        buffer.writeln(headers.map(_csv).join(','));
        for (final raw in rows) {
          final row = Map<String, dynamic>.from(raw as Map);
          buffer.writeln(values(row).map(_csv).join(','));
        }
        buffer.writeln();
      }

      final summary = Map<String, dynamic>.from(
        report['summary'] as Map? ?? const {},
      );

      buffer.writeln('ERP Accounting - Reports Center');
      buffer.writeln(
        'From,' +
            _csv(formatReportDate(_period?.from)) +
            ',To,' +
            _csv(formatReportDate(_period?.to)),
      );
      buffer.writeln();
      buffer.writeln('KPI,Value');
      buffer.writeln('Net Sales,' + (summary['netSales'] ?? 0).toString());
      buffer.writeln(
        'Cost Of Goods Sold,' +
            (summary['costOfGoodsSold'] ?? 0).toString(),
      );
      buffer.writeln(
        'Gross Profit,' + (summary['grossProfit'] ?? 0).toString(),
      );
      buffer.writeln(
        'Net Purchases,' +
            (summary['netPurchases'] ?? 0).toString(),
      );
      buffer.writeln(
        'Treasury Net Flow,' +
            (summary['treasuryNetFlow'] ?? 0).toString(),
      );
      buffer.writeln(
        'Inventory Value,' +
            (summary['inventoryValue'] ?? 0).toString(),
      );
      buffer.writeln();

      section(
        'Products',
        const [
          'SKU',
          'Name',
          'Net Sold Qty',
          'Net Sales',
          'COGS',
          'Gross Profit',
          'Margin %',
          'Net Purchased Qty',
          'Net Purchases',
        ],
        report['products'] as List<dynamic>? ?? const [],
        (row) => [
          row['sku'],
          row['productName'],
          row['netSoldQuantity'],
          row['netSales'],
          row['costOfGoodsSold'],
          row['grossProfit'],
          row['grossMarginPercent'],
          row['netPurchasedQuantity'],
          row['netPurchases'],
        ],
      );

      section(
        'Parties',
        const [
          'Code',
          'Name',
          'Type',
          'Sales',
          'Purchases',
          'Receipts',
          'Payments',
          'Net Exposure',
        ],
        report['parties'] as List<dynamic>? ?? const [],
        (row) => [
          row['code'],
          row['name'],
          row['type'],
          row['sales'],
          row['purchases'],
          row['receipts'],
          row['payments'],
          row['netCommercialFlow'],
        ],
      );

      section(
        'Inventory',
        const [
          'Warehouse',
          'SKU',
          'Product',
          'Opening Qty',
          'In Qty',
          'Out Qty',
          'Closing Qty',
          'Closing Value',
        ],
        report['inventory'] as List<dynamic>? ?? const [],
        (row) => [
          row['warehouseName'],
          row['sku'],
          row['productName'],
          row['openingQuantity'],
          row['inQuantity'],
          row['outQuantity'],
          row['closingQuantity'],
          row['closingValue'],
        ],
      );

      section(
        'Treasury',
        const [
          'Code',
          'Name',
          'Type',
          'Receipts',
          'Payments',
          'Transfers In',
          'Transfers Out',
          'Net Flow',
        ],
        report['treasury'] as List<dynamic>? ?? const [],
        (row) => [
          row['code'],
          row['name'],
          row['type'],
          row['receipts'],
          row['payments'],
          row['transfersIn'],
          row['transfersOut'],
          row['netFlow'],
        ],
      );

      final directory = await getDownloadsDirectory() ??
          await getApplicationDocumentsDirectory();
      final stamp = DateTime.now()
          .toIso8601String()
          .replaceAll(':', '-')
          .split('.')
          .first;
      final file = File(
        directory.path +
            Platform.pathSeparator +
            'erp-report-' +
            stamp +
            '.csv',
      );
      await file.writeAsString(
        '\uFEFF' + buffer.toString(),
        flush: true,
      );

      _message('گزارش CSV ذخیره شد: ' + file.path);
    } catch (error) {
      _message('ذخیره گزارش ناموفق بود: ' + error.toString());
    } finally {
      if (mounted) setState(() => _exporting = false);
    }
  }

  String _csv(dynamic value) {
    final text = value?.toString() ?? '';
    return '"' + text.replaceAll('"', '""') + '"';
  }

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(text)),
    );
  }

  void _open(Widget page) {
    Navigator.of(context).push(
      MaterialPageRoute<void>(builder: (_) => page),
    );
  }

  @override
  Widget build(BuildContext context) {
    final period = _period;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('مرکز گزارش‌ها'),
        ),
        body: _loading || period == null
            ? const Center(child: CircularProgressIndicator())
            : FutureBuilder<Map<String, dynamic>>(
                future: _future,
                builder: (context, snapshot) {
                  final report = snapshot.data;
                  return DefaultTabController(
                    length: 6,
                    child: Column(
                      children: [
                        Padding(
                          padding: const EdgeInsets.fromLTRB(
                            20,
                            16,
                            20,
                            0,
                          ),
                          child: Column(
                            children: [
                              AccountingReportFilterBar(
                                period: period,
                                loading:
                                    snapshot.connectionState !=
                                        ConnectionState.done,
                                onFiscalYearChanged:
                                    _selectFiscalYear,
                                onPickFrom: _pickFrom,
                                onPickTo: _pickTo,
                                onRefresh: _refresh,
                              ),
                              _businessFilters(),
                              _financialShortcuts(),
                              if (snapshot.hasError)
                                Card(
                                  child: Padding(
                                    padding:
                                        const EdgeInsets.all(16),
                                    child: Row(
                                      children: [
                                        const Icon(
                                          Icons.error_outline,
                                        ),
                                        const SizedBox(width: 10),
                                        Expanded(
                                          child: Text(
                                            snapshot.error.toString(),
                                          ),
                                        ),
                                      ],
                                    ),
                                  ),
                                ),
                              if (report != null)
                                _summary(report),
                            ],
                          ),
                        ),
                        if (report != null) ...[
                          const TabBar(
                            isScrollable: true,
                            tabs: [
                              Tab(text: 'کالا و سود'),
                              Tab(text: 'مشتری / تأمین‌کننده'),
                              Tab(text: 'موجودی'),
                              Tab(text: 'خزانه'),
                              Tab(text: 'مرکز هزینه'),
                              Tab(text: 'پروژه'),
                            ],
                          ),
                          Expanded(
                            child: TabBarView(
                              children: [
                                _productsTable(report),
                                _partiesTable(report),
                                _inventoryTable(report),
                                _treasuryTable(report),
                                _dimensionTable(
                                  report,
                                  'costCenters',
                                  'مرکز هزینه',
                                ),
                                _dimensionTable(
                                  report,
                                  'projects',
                                  'پروژه',
                                ),
                              ],
                            ),
                          ),
                        ] else
                          const Expanded(
                            child: Center(
                              child: CircularProgressIndicator(),
                            ),
                          ),
                      ],
                    ),
                  );
                },
              ),
      ),
    );
  }

  Widget _businessFilters() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Wrap(
          spacing: 12,
          runSpacing: 12,
          children: [
            SizedBox(
              width: 230,
              child: DropdownButtonFormField<String?>(
                initialValue: _warehouseId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'انبار',
                  prefixIcon: Icon(Icons.warehouse_outlined),
                ),
                items: [
                  const DropdownMenuItem(
                    value: null,
                    child: Text('همه انبارها'),
                  ),
                  for (final item in _warehouses)
                    DropdownMenuItem(
                      value: item['id']?.toString(),
                      child: Text(
                        item['code'].toString() +
                            ' — ' +
                            item['name'].toString(),
                      ),
                    ),
                ],
                onChanged: (value) {
                  setState(() {
                    _warehouseId = value;
                    _future = _fetch();
                  });
                },
              ),
            ),
            SizedBox(
              width: 260,
              child: DropdownButtonFormField<String?>(
                initialValue: _productId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'کالا / خدمت',
                  prefixIcon: Icon(Icons.inventory_2_outlined),
                ),
                items: [
                  const DropdownMenuItem(
                    value: null,
                    child: Text('همه کالاها'),
                  ),
                  for (final item in _products)
                    DropdownMenuItem(
                      value: item['id']?.toString(),
                      child: Text(
                        item['sku'].toString() +
                            ' — ' +
                            item['name'].toString(),
                      ),
                    ),
                ],
                onChanged: (value) {
                  setState(() {
                    _productId = value;
                    _future = _fetch();
                  });
                },
              ),
            ),
            SizedBox(
              width: 280,
              child: DropdownButtonFormField<String?>(
                initialValue: _detailId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'مشتری / تأمین‌کننده / تفصیلی',
                  prefixIcon: Icon(Icons.people_outline),
                ),
                items: [
                  const DropdownMenuItem(
                    value: null,
                    child: Text('همه تفصیلی‌ها'),
                  ),
                  for (final item in _details)
                    DropdownMenuItem(
                      value: item.id,
                      child: Text(
                        item.code + ' — ' + item.name,
                      ),
                    ),
                ],
                onChanged: (value) {
                  setState(() {
                    _detailId = value;
                    _future = _fetch();
                  });
                },
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _financialShortcuts() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            _shortcut(
              'روزنامه',
              Icons.menu_book_outlined,
              JournalReportPage(
                companyId: widget.companyId,
                accessToken: widget.accessToken,
                localDatabase: widget.localDatabase,
              ),
            ),
            _shortcut(
              'دفتر کل',
              Icons.library_books_outlined,
              GeneralLedgerPage(
                companyId: widget.companyId,
                accessToken: widget.accessToken,
                localDatabase: widget.localDatabase,
              ),
            ),
            _shortcut(
              'تفصیلی',
              Icons.person_search_outlined,
              DetailLedgerPage(
                companyId: widget.companyId,
                accessToken: widget.accessToken,
                localDatabase: widget.localDatabase,
              ),
            ),
            _shortcut(
              'تراز آزمایشی',
              Icons.balance_outlined,
              TrialBalancePage(
                companyId: widget.companyId,
                accessToken: widget.accessToken,
                localDatabase: widget.localDatabase,
              ),
            ),
            _shortcut(
              'سود و زیان',
              Icons.show_chart_outlined,
              ProfitLossPage(
                companyId: widget.companyId,
                accessToken: widget.accessToken,
                localDatabase: widget.localDatabase,
              ),
            ),
            _shortcut(
              'ترازنامه',
              Icons.account_balance_outlined,
              BalanceSheetPage(
                companyId: widget.companyId,
                accessToken: widget.accessToken,
                localDatabase: widget.localDatabase,
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _shortcut(String label, IconData icon, Widget page) {
    return OutlinedButton.icon(
      onPressed: () => _open(page),
      icon: Icon(icon),
      label: Text(label),
    );
  }

  Widget _summary(Map<String, dynamic> report) {
    final summary = Map<String, dynamic>.from(
      report['summary'] as Map? ?? const {},
    );

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Wrap(
          spacing: 12,
          runSpacing: 12,
          children: [
            _Kpi(
              title: 'فروش خالص',
              value: reportNumber(summary['netSales']),
            ),
            _Kpi(
              title: 'بهای تمام‌شده',
              value: reportNumber(summary['costOfGoodsSold']),
            ),
            _Kpi(
              title: 'سود ناخالص',
              value: reportNumber(summary['grossProfit']),
            ),
            _Kpi(
              title: 'خرید خالص',
              value: reportNumber(summary['netPurchases']),
            ),
            _Kpi(
              title: 'خالص جریان خزانه',
              value: reportNumber(summary['treasuryNetFlow']),
            ),
            _Kpi(
              title: 'ارزش موجودی',
              value: reportNumber(summary['inventoryValue']),
            ),
            FilledButton.icon(
              onPressed:
                  _exporting ? null : () => _exportCsv(report),
              icon: _exporting
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                      ),
                    )
                  : const Icon(Icons.download_outlined),
              label: const Text('خروجی CSV / Excel'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _productsTable(Map<String, dynamic> report) {
    return _table(
      report['products'] as List<dynamic>? ?? const [],
      const [
        'کد',
        'کالا',
        'فروش خالص تعداد',
        'فروش خالص مبلغ',
        'بهای تمام‌شده',
        'سود ناخالص',
        'حاشیه سود %',
        'خرید خالص تعداد',
        'خرید خالص مبلغ',
      ],
      (row) => [
        row['sku'],
        row['productName'],
        row['netSoldQuantity'],
        row['netSales'],
        row['costOfGoodsSold'],
        row['grossProfit'],
        row['grossMarginPercent'],
        row['netPurchasedQuantity'],
        row['netPurchases'],
      ],
    );
  }

  Widget _partiesTable(Map<String, dynamic> report) {
    return _table(
      report['parties'] as List<dynamic>? ?? const [],
      const [
        'کد',
        'نام',
        'نوع',
        'فروش',
        'خرید',
        'دریافت',
        'پرداخت',
        'مانده تجاری',
      ],
      (row) => [
        row['code'],
        row['name'],
        _partyType(row['type']?.toString()),
        row['sales'],
        row['purchases'],
        row['receipts'],
        row['payments'],
        row['netCommercialFlow'],
      ],
    );
  }

  Widget _inventoryTable(Map<String, dynamic> report) {
    return _table(
      report['inventory'] as List<dynamic>? ?? const [],
      const [
        'انبار',
        'کد کالا',
        'کالا',
        'اول دوره',
        'ورودی',
        'خروجی',
        'پایان دوره',
        'ارزش پایان دوره',
      ],
      (row) => [
        row['warehouseName'],
        row['sku'],
        row['productName'],
        row['openingQuantity'],
        row['inQuantity'],
        row['outQuantity'],
        row['closingQuantity'],
        row['closingValue'],
      ],
    );
  }

  Widget _treasuryTable(Map<String, dynamic> report) {
    return _table(
      report['treasury'] as List<dynamic>? ?? const [],
      const [
        'کد',
        'صندوق / بانک',
        'نوع',
        'دریافت',
        'پرداخت',
        'انتقال ورودی',
        'انتقال خروجی',
        'خالص جریان',
      ],
      (row) => [
        row['code'],
        row['name'],
        row['type']?.toString() == 'Bank' ? 'بانک' : 'صندوق',
        row['receipts'],
        row['payments'],
        row['transfersIn'],
        row['transfersOut'],
        row['netFlow'],
      ],
    );
  }

  Widget _dimensionTable(
    Map<String, dynamic> report,
    String key,
    String title,
  ) {
    return _table(
      report[key] as List<dynamic>? ?? const [],
      [
        'کد ' + title,
        title,
        'بدهکار',
        'بستانکار',
        'خالص',
      ],
      (row) => [
        row['code'],
        row['name'],
        row['debit'],
        row['credit'],
        row['net'],
      ],
    );
  }

  Widget _table(
    List<dynamic> rawRows,
    List<String> headers,
    List<dynamic> Function(Map<String, dynamic>) values,
  ) {
    if (rawRows.isEmpty) {
      return const Center(
        child: Text(
          'در بازه و فیلتر انتخاب‌شده داده‌ای وجود ندارد.',
        ),
      );
    }

    return Scrollbar(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(20),
        child: SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: DataTable(
            columns: [
              for (final header in headers)
                DataColumn(label: Text(header)),
            ],
            rows: [
              for (final raw in rawRows)
                DataRow(
                  cells: [
                    for (final value
                        in values(
                          Map<String, dynamic>.from(raw as Map),
                        ))
                      DataCell(
                        Text(
                          value is num
                              ? formatReportMoney(value)
                              : (value?.toString() ?? ''),
                        ),
                      ),
                  ],
                ),
            ],
          ),
        ),
      ),
    );
  }

  String _partyType(String? value) {
    switch (value) {
      case 'Customer':
        return 'مشتری';
      case 'Supplier':
        return 'تأمین‌کننده';
      case 'Employee':
        return 'کارمند';
      case 'Bank':
        return 'بانک';
      default:
        return value ?? '';
    }
  }
}

class _Kpi extends StatelessWidget {
  const _Kpi({
    required this.title,
    required this.value,
  });

  final String title;
  final num value;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 190,
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        border: Border.all(
          color: Theme.of(context).colorScheme.outlineVariant,
        ),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: Theme.of(context).textTheme.labelLarge,
          ),
          const SizedBox(height: 8),
          Text(
            formatReportMoney(value),
            style: Theme.of(context).textTheme.titleLarge,
          ),
        ],
      ),
    );
  }
}
