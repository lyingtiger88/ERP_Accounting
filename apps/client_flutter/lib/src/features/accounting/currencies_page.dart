import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';

class CurrenciesPage extends StatefulWidget {
  const CurrenciesPage({
    super.key,
    required this.accessToken,
  });

  final String accessToken;

  @override
  State<CurrenciesPage> createState() => _CurrenciesPageState();
}

class _CurrenciesPageState extends State<CurrenciesPage> {
  final _apiClient = ApiClient();
  late Future<List<Map<String, dynamic>>> _future;
  bool _busy = false;

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

  Future<void> _create() async {
    final code = TextEditingController();
    final name = TextEditingController();
    final symbol = TextEditingController();
    final decimals = TextEditingController(text: '2');

    final result = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('تعریف ارز جدید'),
        content: SizedBox(
          width: 460,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: code,
                textCapitalization: TextCapitalization.characters,
                decoration: const InputDecoration(
                  labelText: 'کد ارز',
                  hintText: 'USD / EUR / AED',
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: name,
                decoration: const InputDecoration(
                  labelText: 'نام ارز',
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: symbol,
                decoration: const InputDecoration(
                  labelText: 'نماد',
                  hintText: r'$ / € / £',
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: decimals,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'تعداد اعشار',
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
            child: const Text('ثبت'),
          ),
        ],
      ),
    );

    if (result != true) {
      code.dispose();
      name.dispose();
      symbol.dispose();
      decimals.dispose();
      return;
    }

    setState(() => _busy = true);

    try {
      await _apiClient.createCurrency(
        bearerToken: widget.accessToken,
        code: code.text,
        name: name.text,
        symbol: symbol.text,
        decimalPlaces: int.tryParse(decimals.text) ?? 2,
      );

      if (!mounted) return;
      setState(_reload);
      _message('ارز جدید ثبت شد.');
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      code.dispose();
      name.dispose();
      symbol.dispose();
      decimals.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _setBase(Map<String, dynamic> item) async {
    if (_busy || (item['isBase'] as bool? ?? false)) return;

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('تغییر ارز پایه'),
        content: Text(
          'ارز پایه به \${item['code']} تغییر کند؟\n\n'
          'این تغییر فقط قبل از ایجاد تاریخچه اسناد حسابداری مجاز است تا مبالغ تاریخی بازتفسیر نشوند.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('تغییر ارز پایه'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    setState(() => _busy = true);

    try {
      await _apiClient.setBaseCurrency(
        bearerToken: widget.accessToken,
        currencyId: item['id'].toString(),
      );

      if (!mounted) return;
      setState(_reload);
      _message('ارز پایه تغییر کرد.');
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _toggle(Map<String, dynamic> item) async {
    if (_busy || (item['isBase'] as bool? ?? false)) return;

    setState(() => _busy = true);

    try {
      await _apiClient.setCurrencyState(
        bearerToken: widget.accessToken,
        currencyId: item['id'].toString(),
        isActive: !(item['isActive'] as bool? ?? true),
      );

      if (!mounted) return;
      setState(_reload);
    } on ApiException catch (error) {
      _message(error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(text)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('ارزهای بین‌المللی'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _busy ? null : () => setState(_reload),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _busy ? null : _create,
          icon: const Icon(Icons.add),
          label: const Text('ارز جدید'),
        ),
        body: FutureBuilder<List<Map<String, dynamic>>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(
                child: Text(snapshot.error.toString()),
              );
            }

            final items =
                snapshot.data ?? const <Map<String, dynamic>>[];

            if (items.isEmpty) {
              return const Center(
                child: Text('ارزی تعریف نشده است.'),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: items.length,
              separatorBuilder: (_, __) => const SizedBox(height: 10),
              itemBuilder: (context, index) {
                final item = items[index];
                final isBase = item['isBase'] as bool? ?? false;
                final active = item['isActive'] as bool? ?? true;

                return Card(
                  child: ListTile(
                    leading: CircleAvatar(
                      child: Text(
                        item['symbol']?.toString().isNotEmpty == true
                            ? item['symbol'].toString()
                            : item['code'].toString().substring(0, 1),
                      ),
                    ),
                    title: Text(
                      item['code'].toString() +
                          ' — ' +
                          item['name'].toString(),
                    ),
                    subtitle: Text(
                      'اعشار: \${item['decimalPlaces']}'
                      '\${isBase ? ' • ارز پایه شرکت' : ''}',
                    ),
                    trailing: Wrap(
                      spacing: 8,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        Chip(
                          avatar: Icon(
                            active
                                ? Icons.check_circle_outline
                                : Icons.pause_circle_outline,
                            size: 16,
                          ),
                          label: Text(active ? 'فعال' : 'غیرفعال'),
                        ),
                        if (isBase)
                          const Chip(
                            avatar: Icon(
                              Icons.home_outlined,
                              size: 16,
                            ),
                            label: Text('پایه'),
                          ),
                        PopupMenuButton<String>(
                          enabled: !_busy,
                          onSelected: (value) {
                            if (value == 'base') {
                              _setBase(item);
                            } else if (value == 'toggle') {
                              _toggle(item);
                            }
                          },
                          itemBuilder: (_) => [
                            if (!isBase)
                              const PopupMenuItem(
                                value: 'base',
                                child: Text('انتخاب به‌عنوان ارز پایه'),
                              ),
                            if (!isBase)
                              PopupMenuItem(
                                value: 'toggle',
                                child: Text(
                                  active ? 'غیرفعال‌کردن' : 'فعال‌کردن',
                                ),
                              ),
                          ],
                        ),
                      ],
                    ),
                  ),
                );
              },
            );
          },
        ),
      ),
    );
  }
}
