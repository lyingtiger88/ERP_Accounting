import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/database/local_database.dart';
import '../../core/demo/demo_mode.dart';
import '../../core/demo/local_demo_business_engine.dart';
import '../accounting/report_support.dart';

class TreasuryHomePage extends StatefulWidget {
  const TreasuryHomePage({
    super.key,
    required this.companyId,
    required this.accessToken,
    required this.localDatabase,
  });

  final String companyId;
  final String accessToken;
  final LocalDatabase localDatabase;

  @override
  State<TreasuryHomePage> createState() => _TreasuryHomePageState();
}

class _TreasuryHomePageState extends State<TreasuryHomePage> {
  final _api = ApiClient();

  bool _loading = true;
  bool _busy = false;
  List<Map<String, dynamic>> _treasuryAccounts = const [];
  List<Map<String, dynamic>> _transactions = const [];
  List<CachedAccount> _ledgerAccounts = const [];
  List<CachedFiscalYear> _fiscalYears = const [];
  List<CachedDetailAccount> _details = const [];

  bool get _isDemo => DemoMode.isDemoToken(widget.accessToken);

  @override
  void initState() {
    super.initState();
    _reload();
  }

  Future<void> _reload() async {
    if (mounted) {
      setState(() => _loading = true);
    }

    try {
      if (!_isDemo) {
        final remoteAccounts = await _api.getAccounts(
          bearerToken: widget.accessToken,
        );
        final remoteYears = await _api.getFiscalYears(
          bearerToken: widget.accessToken,
        );
        final remoteDetails = await _api.getDetailAccounts(
          bearerToken: widget.accessToken,
        );

        await widget.localDatabase.replaceAccounts(
          companyId: widget.companyId,
          accounts: remoteAccounts,
        );
        await widget.localDatabase.replaceFiscalYears(
          companyId: widget.companyId,
          fiscalYears: remoteYears,
        );
        await widget.localDatabase.replaceDetailAccounts(
          companyId: widget.companyId,
          details: remoteDetails,
        );

        final treasuryAccounts = await _api.getTreasuryAccounts(
          bearerToken: widget.accessToken,
        );
        final transactions = await _api.getTreasuryTransactions(
          bearerToken: widget.accessToken,
        );

        await widget.localDatabase.replaceStoreEntities(
          companyId: widget.companyId,
          entityType: 'TreasuryAccount',
          items: treasuryAccounts,
        );
        await widget.localDatabase.replaceStoreEntities(
          companyId: widget.companyId,
          entityType: 'TreasuryTransaction',
          items: transactions,
        );
      }

      final treasuryAccounts =
          await widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'TreasuryAccount',
      );
      final transactions =
          await widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'TreasuryTransaction',
      );
      final ledgerAccounts =
          await widget.localDatabase.getCachedAccounts(widget.companyId);
      final fiscalYears =
          await widget.localDatabase.getCachedFiscalYears(widget.companyId);
      final details =
          await widget.localDatabase.getCachedDetailAccounts(widget.companyId);

      transactions.sort((a, b) {
        final da = a['documentDate']?.toString() ?? '';
        final db = b['documentDate']?.toString() ?? '';
        final byDate = db.compareTo(da);
        if (byDate != 0) return byDate;
        return (b['createdAt']?.toString() ?? '')
            .compareTo(a['createdAt']?.toString() ?? '');
      });

      if (!mounted) return;
      setState(() {
        _treasuryAccounts = treasuryAccounts;
        _transactions = transactions;
        _ledgerAccounts = ledgerAccounts;
        _fiscalYears = fiscalYears;
        _details = details;
        _loading = false;
      });
    } on ApiException catch (error) {
      final treasuryAccounts =
          await widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'TreasuryAccount',
      );
      final transactions =
          await widget.localDatabase.getCachedStoreEntities(
        companyId: widget.companyId,
        entityType: 'TreasuryTransaction',
      );
      final ledgerAccounts =
          await widget.localDatabase.getCachedAccounts(widget.companyId);
      final fiscalYears =
          await widget.localDatabase.getCachedFiscalYears(widget.companyId);
      final details =
          await widget.localDatabase.getCachedDetailAccounts(widget.companyId);

      if (!mounted) return;
      setState(() {
        _treasuryAccounts = treasuryAccounts;
        _transactions = transactions;
        _ledgerAccounts = ledgerAccounts;
        _fiscalYears = fiscalYears;
        _details = details;
        _loading = false;
      });
      _message(
        error.statusCode == null
            ? 'اتصال سرور در دسترس نیست؛ داده محلی نمایش داده شد.'
            : error.message,
      );
    }
  }

  num _baseAmount(Map<String, dynamic> tx) {
    final amount = reportNumber(tx['amount']);
    final rate = reportNumber(tx['exchangeRate']);
    return amount * (rate > 0 ? rate : 1);
  }

  num _balanceFor(String treasuryId) {
    num balance = 0;
    for (final tx in _transactions) {
      if (tx['status']?.toString() != 'Posted') continue;
      final amount = _baseAmount(tx);
      switch (tx['type']?.toString()) {
        case 'Receipt':
          if (tx['toTreasuryAccountId']?.toString() == treasuryId) {
            balance += amount;
          }
          break;
        case 'Payment':
          if (tx['fromTreasuryAccountId']?.toString() == treasuryId) {
            balance -= amount;
          }
          break;
        case 'Transfer':
          if (tx['toTreasuryAccountId']?.toString() == treasuryId) {
            balance += amount;
          }
          if (tx['fromTreasuryAccountId']?.toString() == treasuryId) {
            balance -= amount;
          }
          break;
      }
    }
    return balance;
  }

  Future<void> _editTreasuryAccount([
    Map<String, dynamic>? current,
  ]) async {
    final code = TextEditingController(
      text: current?['code']?.toString() ?? '',
    );
    final name = TextEditingController(
      text: current?['name']?.toString() ?? '',
    );
    var type = current?['type']?.toString() ?? 'Cashbox';
    String? ledgerId = current?['ledgerAccountId']?.toString();
    var isActive = current?['isActive'] as bool? ?? true;

    final assetAccounts = _ledgerAccounts
        .where(
          (item) =>
              item.isActive &&
              item.isPostable &&
              item.type == 'Asset',
        )
        .toList(growable: false);

    if (ledgerId == null && assetAccounts.isNotEmpty) {
      ledgerId = assetAccounts.first.id;
    }

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(
            current == null
                ? 'تعریف صندوق / بانک'
                : 'ویرایش صندوق / بانک',
          ),
          content: SizedBox(
            width: 560,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextField(
                    controller: code,
                    textDirection: TextDirection.ltr,
                    decoration: const InputDecoration(
                      labelText: 'کد',
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: name,
                    decoration: const InputDecoration(
                      labelText: 'نام',
                    ),
                  ),
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    initialValue: type,
                    decoration: const InputDecoration(
                      labelText: 'نوع',
                    ),
                    items: const [
                      DropdownMenuItem(
                        value: 'Cashbox',
                        child: Text('صندوق'),
                      ),
                      DropdownMenuItem(
                        value: 'Bank',
                        child: Text('حساب بانکی'),
                      ),
                    ],
                    onChanged: (value) {
                      if (value != null) {
                        setDialogState(() => type = value);
                      }
                    },
                  ),
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    initialValue: ledgerId,
                    isExpanded: true,
                    decoration: const InputDecoration(
                      labelText: 'حساب حسابداری متصل',
                    ),
                    items: [
                      for (final item in assetAccounts)
                        DropdownMenuItem(
                          value: item.id,
                          child: Text(
                            item.code + ' — ' + item.name,
                          ),
                        ),
                    ],
                    onChanged: (value) {
                      setDialogState(() => ledgerId = value);
                    },
                  ),
                  if (current != null)
                    SwitchListTile(
                      value: isActive,
                      title: const Text('فعال'),
                      onChanged: (value) {
                        setDialogState(() => isActive = value);
                      },
                    ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('انصراف'),
            ),
            FilledButton(
              onPressed: () {
                if (code.text.trim().isEmpty ||
                    name.text.trim().isEmpty ||
                    ledgerId == null) {
                  return;
                }
                Navigator.pop(context, true);
              },
              child: const Text('ذخیره'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true) {
      code.dispose();
      name.dispose();
      return;
    }

    setState(() => _busy = true);
    try {
      if (_isDemo) {
        await LocalDemoBusinessEngine(
          localDatabase: widget.localDatabase,
        ).saveTreasuryAccount(
          id: current?['id']?.toString(),
          code: code.text,
          name: name.text,
          type: type,
          ledgerAccountId: ledgerId!,
          isActive: isActive,
        );
      } else if (current == null) {
        await _api.createTreasuryAccount(
          bearerToken: widget.accessToken,
          code: code.text.trim(),
          name: name.text.trim(),
          type: type,
          ledgerAccountId: ledgerId!,
        );
      } else {
        await _api.updateTreasuryAccount(
          bearerToken: widget.accessToken,
          accountId: current['id'].toString(),
          code: code.text.trim(),
          name: name.text.trim(),
          type: type,
          ledgerAccountId: ledgerId!,
          currencyId: current['currencyId']?.toString(),
          isActive: isActive,
        );
      }

      if (!mounted) return;
      _message('اطلاعات صندوق/بانک ذخیره شد.');
      await _reload();
    } on ApiException catch (error) {
      _message(error.message);
    } on StateError catch (error) {
      _message(error.message);
    } finally {
      code.dispose();
      name.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _newTransaction(String initialType) async {
    final activeTreasury = _treasuryAccounts
        .where((item) => item['isActive'] as bool? ?? true)
        .toList(growable: false);
    if (activeTreasury.isEmpty) {
      _message('ابتدا حداقل یک صندوق یا حساب بانکی تعریف کنید.');
      return;
    }

    final openYears = _fiscalYears
        .where((item) => !item.isClosed && !item.isFinalized)
        .toList(growable: false);
    if (openYears.isEmpty) {
      _message('سال مالی باز برای ثبت عملیات وجود ندارد.');
      return;
    }

    var type = initialType;
    var date = DateTime.now();
    String fiscalYearId = openYears
        .firstWhere(
          (item) => item.contains(date),
          orElse: () => openYears.first,
        )
        .id;
    String? fromId =
        type == 'Payment' || type == 'Transfer'
            ? activeTreasury.first['id'].toString()
            : null;
    String? toId =
        type == 'Receipt'
            ? activeTreasury.first['id'].toString()
            : type == 'Transfer' && activeTreasury.length > 1
                ? activeTreasury[1]['id'].toString()
                : null;

    final counterAccounts = _ledgerAccounts
        .where(
          (item) =>
              item.isActive &&
              item.isPostable &&
              !activeTreasury.any(
                (treasury) =>
                    treasury['ledgerAccountId']?.toString() == item.id,
              ),
        )
        .toList(growable: false);

    String? counterId =
        type == 'Transfer' || counterAccounts.isEmpty
            ? null
            : counterAccounts.first.id;
    String? detailId;
    final amount = TextEditingController();
    final description = TextEditingController();

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) {
          void normalizeType(String next) {
            type = next;
            if (type == 'Receipt') {
              fromId = null;
              toId ??= activeTreasury.first['id'].toString();
              counterId ??= counterAccounts.isEmpty
                  ? null
                  : counterAccounts.first.id;
            } else if (type == 'Payment') {
              toId = null;
              fromId ??= activeTreasury.first['id'].toString();
              counterId ??= counterAccounts.isEmpty
                  ? null
                  : counterAccounts.first.id;
            } else {
              fromId ??= activeTreasury.first['id'].toString();
              if (activeTreasury.length > 1) {
                toId ??= activeTreasury
                    .firstWhere(
                      (item) =>
                          item['id'].toString() != fromId,
                      orElse: () => activeTreasury.first,
                    )['id']
                    .toString();
              }
              counterId = null;
              detailId = null;
            }
          }

          return AlertDialog(
            title: const Text('ثبت عملیات بانک و صندوق'),
            content: SizedBox(
              width: 680,
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    SegmentedButton<String>(
                      segments: const [
                        ButtonSegment(
                          value: 'Receipt',
                          label: Text('دریافت'),
                          icon: Icon(Icons.south_west),
                        ),
                        ButtonSegment(
                          value: 'Payment',
                          label: Text('پرداخت'),
                          icon: Icon(Icons.north_east),
                        ),
                        ButtonSegment(
                          value: 'Transfer',
                          label: Text('انتقال'),
                          icon: Icon(Icons.swap_horiz),
                        ),
                      ],
                      selected: {type},
                      onSelectionChanged: (value) {
                        setDialogState(() {
                          normalizeType(value.first);
                        });
                      },
                    ),
                    const SizedBox(height: 16),
                    DropdownButtonFormField<String>(
                      initialValue: fiscalYearId,
                      isExpanded: true,
                      decoration: const InputDecoration(
                        labelText: 'سال مالی',
                      ),
                      items: [
                        for (final year in openYears)
                          DropdownMenuItem(
                            value: year.id,
                            child: Text(year.name),
                          ),
                      ],
                      onChanged: (value) {
                        if (value != null) {
                          setDialogState(() => fiscalYearId = value);
                        }
                      },
                    ),
                    const SizedBox(height: 12),
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
                      label: Text(
                        'تاریخ: ' + formatReportDate(date),
                      ),
                    ),
                    const SizedBox(height: 12),
                    if (type == 'Payment' || type == 'Transfer')
                      DropdownButtonFormField<String>(
                        initialValue: fromId,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'مبدا',
                        ),
                        items: [
                          for (final item in activeTreasury)
                            DropdownMenuItem(
                              value: item['id'].toString(),
                              child: Text(
                                item['code'].toString() +
                                    ' — ' +
                                    item['name'].toString(),
                              ),
                            ),
                        ],
                        onChanged: (value) {
                          setDialogState(() => fromId = value);
                        },
                      ),
                    if (type == 'Payment' || type == 'Transfer')
                      const SizedBox(height: 12),
                    if (type == 'Receipt' || type == 'Transfer')
                      DropdownButtonFormField<String>(
                        initialValue: toId,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'مقصد',
                        ),
                        items: [
                          for (final item in activeTreasury)
                            DropdownMenuItem(
                              value: item['id'].toString(),
                              child: Text(
                                item['code'].toString() +
                                    ' — ' +
                                    item['name'].toString(),
                              ),
                            ),
                        ],
                        onChanged: (value) {
                          setDialogState(() => toId = value);
                        },
                      ),
                    if (type == 'Receipt' || type == 'Transfer')
                      const SizedBox(height: 12),
                    if (type != 'Transfer')
                      DropdownButtonFormField<String>(
                        initialValue: counterId,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'حساب مقابل',
                        ),
                        items: [
                          for (final item in counterAccounts)
                            DropdownMenuItem(
                              value: item.id,
                              child: Text(
                                item.code + ' — ' + item.name,
                              ),
                            ),
                        ],
                        onChanged: (value) {
                          setDialogState(() => counterId = value);
                        },
                      ),
                    if (type != 'Transfer')
                      const SizedBox(height: 12),
                    if (type != 'Transfer')
                      DropdownButtonFormField<String?>(
                        initialValue: detailId,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'تفصیلی (اختیاری)',
                        ),
                        items: [
                          const DropdownMenuItem(
                            value: null,
                            child: Text('بدون تفصیلی'),
                          ),
                          for (final item
                              in _details.where((x) => x.isActive))
                            DropdownMenuItem(
                              value: item.id,
                              child: Text(
                                item.code + ' — ' + item.name,
                              ),
                            ),
                        ],
                        onChanged: (value) {
                          setDialogState(() => detailId = value);
                        },
                      ),
                    if (type != 'Transfer')
                      const SizedBox(height: 12),
                    TextField(
                      controller: amount,
                      keyboardType:
                          const TextInputType.numberWithOptions(
                        decimal: true,
                      ),
                      textDirection: TextDirection.ltr,
                      decoration: const InputDecoration(
                        labelText: 'مبلغ',
                        suffixText: 'ریال',
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: description,
                      maxLines: 2,
                      decoration: const InputDecoration(
                        labelText: 'شرح',
                      ),
                    ),
                  ],
                ),
              ),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(context, false),
                child: const Text('انصراف'),
              ),
              FilledButton(
                onPressed: () {
                  final value = num.tryParse(
                        amount.text.replaceAll(',', '').trim(),
                      ) ??
                      0;
                  if (value <= 0) return;
                  if (type == 'Receipt' &&
                      (toId == null || counterId == null)) {
                    return;
                  }
                  if (type == 'Payment' &&
                      (fromId == null || counterId == null)) {
                    return;
                  }
                  if (type == 'Transfer' &&
                      (fromId == null ||
                          toId == null ||
                          fromId == toId)) {
                    return;
                  }
                  Navigator.pop(context, true);
                },
                child: const Text('ثبت قطعی'),
              ),
            ],
          );
        },
      ),
    );

    if (confirmed != true) {
      amount.dispose();
      description.dispose();
      return;
    }

    final value = num.parse(
      amount.text.replaceAll(',', '').trim(),
    );

    setState(() => _busy = true);
    try {
      if (_isDemo) {
        await LocalDemoBusinessEngine(
          localDatabase: widget.localDatabase,
        ).postTreasuryTransaction(
          fiscalYearId: fiscalYearId,
          documentDate: date,
          type: type,
          amount: value,
          description: description.text,
          fromTreasuryAccountId: fromId,
          toTreasuryAccountId: toId,
          counterAccountId: counterId,
          detailAccountId: detailId,
        );
      } else {
        await _api.postTreasuryTransaction(
          bearerToken: widget.accessToken,
          fiscalYearId: fiscalYearId,
          documentDate: date,
          type: type,
          amount: value,
          description: description.text.trim().isEmpty
              ? null
              : description.text.trim(),
          fromTreasuryAccountId: fromId,
          toTreasuryAccountId: toId,
          counterAccountId: counterId,
          detailAccountId: detailId,
        );
      }

      if (!mounted) return;
      _message('عملیات خزانه ثبت و سند حسابداری ایجاد شد.');
      await _reload();
    } on ApiException catch (error) {
      _message(error.message);
    } on StateError catch (error) {
      _message(error.message);
    } finally {
      amount.dispose();
      description.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(text)),
    );
  }

  String _typeTitle(String? type) {
    switch (type) {
      case 'Receipt':
        return 'دریافت';
      case 'Payment':
        return 'پرداخت';
      case 'Transfer':
        return 'انتقال';
      default:
        return type ?? '';
    }
  }

  IconData _typeIcon(String? type) {
    switch (type) {
      case 'Receipt':
        return Icons.south_west;
      case 'Payment':
        return Icons.north_east;
      case 'Transfer':
        return Icons.swap_horiz;
      default:
        return Icons.receipt_long_outlined;
    }
  }

  String _transactionParties(Map<String, dynamic> item) {
    if (item['type']?.toString() == 'Transfer') {
      return (item['fromTreasuryAccountName']?.toString() ?? '?') +
          ' ←→ ' +
          (item['toTreasuryAccountName']?.toString() ?? '?');
    }

    return item['type']?.toString() == 'Receipt'
        ? (item['toTreasuryAccountName']?.toString() ?? '?')
        : (item['fromTreasuryAccountName']?.toString() ?? '?');
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('بانک و صندوق'),
          actions: [
            IconButton(
              tooltip: 'بازخوانی',
              onPressed: _busy ? null : _reload,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _busy ? null : () => _newTransaction('Receipt'),
          icon: const Icon(Icons.add),
          label: const Text('عملیات جدید'),
        ),
        body: _loading
            ? const Center(child: CircularProgressIndicator())
            : DefaultTabController(
                length: 2,
                child: Column(
                  children: [
                    if (_isDemo)
                      Container(
                        width: double.infinity,
                        padding: const EdgeInsets.symmetric(
                          horizontal: 16,
                          vertical: 10,
                        ),
                        child: const Text(
                          'حالت تست آفلاین • عملیات خزانه کاملاً محلی است',
                        ),
                      ),
                    Padding(
                      padding: const EdgeInsets.all(16),
                      child: Wrap(
                        spacing: 10,
                        runSpacing: 10,
                        children: [
                          FilledButton.icon(
                            onPressed:
                                _busy ? null : () => _newTransaction('Receipt'),
                            icon: const Icon(Icons.south_west),
                            label: const Text('دریافت'),
                          ),
                          FilledButton.tonalIcon(
                            onPressed:
                                _busy ? null : () => _newTransaction('Payment'),
                            icon: const Icon(Icons.north_east),
                            label: const Text('پرداخت'),
                          ),
                          OutlinedButton.icon(
                            onPressed: _busy
                                ? null
                                : () => _newTransaction('Transfer'),
                            icon: const Icon(Icons.swap_horiz),
                            label: const Text('انتقال'),
                          ),
                          OutlinedButton.icon(
                            onPressed:
                                _busy ? null : () => _editTreasuryAccount(),
                            icon: const Icon(
                              Icons.account_balance_wallet_outlined,
                            ),
                            label: const Text('تعریف صندوق / بانک'),
                          ),
                        ],
                      ),
                    ),
                    const TabBar(
                      tabs: [
                        Tab(
                          icon: Icon(Icons.account_balance_wallet_outlined),
                          text: 'صندوق‌ها و بانک‌ها',
                        ),
                        Tab(
                          icon: Icon(Icons.receipt_long_outlined),
                          text: 'گردش عملیات',
                        ),
                      ],
                    ),
                    Expanded(
                      child: TabBarView(
                        children: [
                          _accountsTab(),
                          _transactionsTab(),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
      ),
    );
  }

  Widget _accountsTab() {
    if (_treasuryAccounts.isEmpty) {
      return const Center(
        child: Text('هنوز صندوق یا حساب بانکی تعریف نشده است.'),
      );
    }

    return ListView.separated(
      padding: const EdgeInsets.all(20),
      itemCount: _treasuryAccounts.length,
      separatorBuilder: (_, _) => const SizedBox(height: 10),
      itemBuilder: (context, index) {
        final item = _treasuryAccounts[index];
        final id = item['id'].toString();
        final active = item['isActive'] as bool? ?? true;

        return Card(
          child: ListTile(
            leading: CircleAvatar(
              child: Icon(
                item['type']?.toString() == 'Bank'
                    ? Icons.account_balance_outlined
                    : Icons.account_balance_wallet_outlined,
              ),
            ),
            title: Text(
              item['code'].toString() +
                  ' — ' +
                  item['name'].toString(),
            ),
            subtitle: Text(
              (item['ledgerAccountCode']?.toString() ?? '') +
                  ' • ' +
                  (item['ledgerAccountName']?.toString() ?? '') +
                  ' • ' +
                  (active ? 'فعال' : 'غیرفعال'),
            ),
            trailing: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  formatReportMoney(_balanceFor(id)) + ' ریال',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(width: 10),
                IconButton(
                  tooltip: 'ویرایش',
                  onPressed: _busy
                      ? null
                      : () => _editTreasuryAccount(item),
                  icon: const Icon(Icons.edit_outlined),
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _transactionsTab() {
    if (_transactions.isEmpty) {
      return const Center(
        child: Text('هنوز عملیات خزانه‌ای ثبت نشده است.'),
      );
    }

    return ListView.separated(
      padding: const EdgeInsets.all(20),
      itemCount: _transactions.length,
      separatorBuilder: (_, _) => const SizedBox(height: 10),
      itemBuilder: (context, index) {
        final item = _transactions[index];
        return Card(
          child: ListTile(
            leading: CircleAvatar(
              child: Icon(_typeIcon(item['type']?.toString())),
            ),
            title: Text(
              item['number'].toString() +
                  ' • ' +
                  _typeTitle(item['type']?.toString()),
            ),
            subtitle: Text(
              formatReportDate(
                    DateTime.parse(item['documentDate'].toString()),
                  ) +
                  ' • ' +
                  _transactionParties(item) +
                  (item['detailAccountName'] == null
                      ? ''
                      : ' • ' + item['detailAccountName'].toString()) +
                  (item['description'] == null
                      ? ''
                      : '
' + item['description'].toString()),
            ),
            isThreeLine: item['description'] != null,
            trailing: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(
                  formatReportMoney(reportNumber(item['amount'])) +
                      ' ' +
                      (item['currencyCode']?.toString() ?? 'IRR'),
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                if (item['accountingJournalNumber'] != null)
                  Text(
                    'سند ' +
                        item['accountingJournalNumber'].toString(),
                  ),
              ],
            ),
          ),
        );
      },
    );
  }
}
