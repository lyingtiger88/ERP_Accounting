import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import 'report_support.dart';

class CurrencyRatesPage extends StatefulWidget {
  const CurrencyRatesPage({
    super.key,
    required this.accessToken,
  });

  final String accessToken;

  @override
  State<CurrencyRatesPage> createState() => _CurrencyRatesPageState();
}

class _CurrencyRatesPageState extends State<CurrencyRatesPage> {
  final _apiClient = ApiClient();

  List<Map<String, dynamic>> _currencies = const [];
  List<Map<String, dynamic>> _rates = const [];
  String? _currencyId;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _initialize();
  }

  Future<void> _initialize() async {
    try {
      final currencies = await _apiClient.getCurrencies(
        bearerToken: widget.accessToken,
      );

      String? selected;
      for (final item in currencies) {
        if ((item['isActive'] as bool? ?? true) &&
            !(item['isBase'] as bool? ?? false)) {
          selected = item['id'].toString();
          break;
        }
      }

      if (!mounted) return;

      setState(() {
        _currencies = currencies;
        _currencyId = selected;
      });

      await _loadRates();
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.toString();
        _loading = false;
      });
    }
  }

  Map<String, dynamic>? _currency(String? id) {
    if (id == null) return null;
    for (final item in _currencies) {
      if (item['id'].toString() == id) return item;
    }
    return null;
  }

  Future<void> _loadRates() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final rates = await _apiClient.getCurrencyRates(
        bearerToken: widget.accessToken,
        currencyId: _currencyId,
      );

      if (!mounted) return;
      setState(() => _rates = rates);
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _addRate() async {
    final currencyId = _currencyId;
    final currency = _currency(currencyId);
    if (currencyId == null || currency == null) return;

    var date = DateTime.now();
    final buy = TextEditingController();
    final sell = TextEditingController();
    final accounting = TextEditingController();
    final source = TextEditingController(text: 'Manual');

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(
            'نرخ \${currency['code']}',
          ),
          content: SizedBox(
            width: 480,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                OutlinedButton.icon(
                  onPressed: () async {
                    final picked = await showDatePicker(
                      context: context,
                      initialDate: date,
                      firstDate: DateTime(2000),
                      lastDate: DateTime(2100),
                    );
                    if (picked != null) {
                      setDialogState(() => date = picked);
                    }
                  },
                  icon: const Icon(Icons.calendar_month_outlined),
                  label: Text(formatReportDate(date)),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: buy,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: const InputDecoration(
                    labelText: 'نرخ خرید',
                  ),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: sell,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: const InputDecoration(
                    labelText: 'نرخ فروش',
                  ),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: accounting,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: const InputDecoration(
                    labelText: 'نرخ حسابداری',
                    helperText:
                        'مبلغ ارز پایه برای هر یک واحد ارز خارجی',
                  ),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: source,
                  decoration: const InputDecoration(
                    labelText: 'منبع / نوع نرخ',
                    hintText: 'Manual / Bank / Market',
                  ),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('انصراف'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('ثبت نرخ'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true) {
      buy.dispose();
      sell.dispose();
      accounting.dispose();
      source.dispose();
      return;
    }

    num parse(String text) =>
        num.tryParse(text.replaceAll(',', '').trim()) ?? 0;

    setState(() => _busy = true);

    try {
      await _apiClient.saveCurrencyRate(
        bearerToken: widget.accessToken,
        currencyId: currencyId,
        rateDate: date,
        buyRate: parse(buy.text),
        sellRate: parse(sell.text),
        accountingRate: parse(accounting.text),
        source: source.text.trim().isEmpty
            ? 'Manual'
            : source.text.trim(),
      );

      await _loadRates();
      _message('نرخ ارز ثبت شد.');
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      buy.dispose();
      sell.dispose();
      accounting.dispose();
      source.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String value) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(value)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final activeCurrencies = _currencies
        .where((x) => x['isActive'] as bool? ?? true)
        .toList(growable: false);

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('نرخ ارز'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _busy ? null : _loadRates,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: _currencyId == null
            ? null
            : FloatingActionButton.extended(
                onPressed: _busy ? null : _addRate,
                icon: const Icon(Icons.add_chart_outlined),
                label: const Text('ثبت نرخ'),
              ),
        body: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: DropdownButtonFormField<String>(
                  initialValue: _currencyId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'ارز',
                    prefixIcon: Icon(Icons.currency_exchange),
                  ),
                  items: [
                    for (final item in activeCurrencies)
                      DropdownMenuItem(
                        value: item['id'].toString(),
                        child: Text(
                          item['code'].toString() +
                              ' — ' +
                              item['name'].toString() +
                              ((item['isBase'] as bool? ?? false)
                                  ? ' (پایه)'
                                  : ''),
                        ),
                      ),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) async {
                          setState(() => _currencyId = value);
                          await _loadRates();
                        },
                ),
              ),
            ),
            const SizedBox(height: 12),
            if (_error != null)
              Card(
                child: ListTile(
                  leading: Icon(
                    Icons.error_outline,
                    color: Theme.of(context).colorScheme.error,
                  ),
                  title: const Text('خطا در دریافت نرخ‌ها'),
                  subtitle: Text(_error!),
                ),
              )
            else if (_loading)
              const Padding(
                padding: EdgeInsets.all(40),
                child: Center(child: CircularProgressIndicator()),
              )
            else if (_rates.isEmpty)
              const Padding(
                padding: EdgeInsets.all(40),
                child: Center(
                  child: Text('برای این ارز هنوز نرخی ثبت نشده است.'),
                ),
              )
            else
              Card(
                child: SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: DataTable(
                    columns: const [
                      DataColumn(label: Text('تاریخ')),
                      DataColumn(label: Text('ارز')),
                      DataColumn(
                        numeric: true,
                        label: Text('خرید'),
                      ),
                      DataColumn(
                        numeric: true,
                        label: Text('فروش'),
                      ),
                      DataColumn(
                        numeric: true,
                        label: Text('حسابداری'),
                      ),
                      DataColumn(label: Text('منبع')),
                    ],
                    rows: [
                      for (final rate in _rates)
                        DataRow(
                          cells: [
                            DataCell(
                              Text(
                                formatReportDate(
                                  DateTime.parse(
                                    rate['rateDate'].toString(),
                                  ),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(rate['currencyCode'].toString()),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(rate['buyRate']),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(rate['sellRate']),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(
                                formatReportMoney(
                                  reportNumber(
                                    rate['accountingRate'],
                                  ),
                                ),
                              ),
                            ),
                            DataCell(
                              Text(rate['source'].toString()),
                            ),
                          ],
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
