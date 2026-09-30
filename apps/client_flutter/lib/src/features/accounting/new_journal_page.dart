import 'package:flutter/material.dart';

import '../../core/database/local_database.dart';

class NewJournalPage extends StatefulWidget {
  const NewJournalPage({
    super.key,
    required this.companyId,
    required this.localDatabase,
  });

  final String companyId;
  final LocalDatabase localDatabase;

  @override
  State<NewJournalPage> createState() => _NewJournalPageState();
}

class _NewJournalPageState extends State<NewJournalPage> {
  final _description = TextEditingController();
  final _rows = <_JournalRowEditor>[];

  DateTime _documentDate = DateTime.now();
  bool _busy = false;
  late Future<List<CachedAccount>> _accountsFuture;

  @override
  void initState() {
    super.initState();
    _accountsFuture =
        widget.localDatabase.getCachedAccounts(widget.companyId);
    _addRow();
    _addRow();
  }

  @override
  void dispose() {
    _description.dispose();
    for (final row in _rows) {
      row.dispose();
    }
    super.dispose();
  }

  void _addRow() {
    setState(() {
      _rows.add(_JournalRowEditor());
    });
  }

  void _removeRow(int index) {
    if (_rows.length <= 2) return;

    setState(() {
      _rows.removeAt(index).dispose();
    });
  }

  int _parseAmount(String raw) {
    var normalized = raw
        .replaceAll(',', '')
        .replaceAll('٬', '')
        .replaceAll(' ', '');

    const persian = '۰۱۲۳۴۵۶۷۸۹';
    const arabic = '٠١٢٣٤٥٦٧٨٩';

    for (var i = 0; i < 10; i++) {
      normalized = normalized
          .replaceAll(persian[i], i.toString())
          .replaceAll(arabic[i], i.toString());
    }

    return int.tryParse(normalized) ?? 0;
  }

  int get _debitTotal => _rows.fold(
        0,
        (sum, row) => sum + _parseAmount(row.debit.text),
      );

  int get _creditTotal => _rows.fold(
        0,
        (sum, row) => sum + _parseAmount(row.credit.text),
      );

  bool get _balanced =>
      _debitTotal > 0 && _debitTotal == _creditTotal;

  String _money(int value) {
    final raw = value.abs().toString();
    final buffer = StringBuffer();
    for (var i = 0; i < raw.length; i++) {
      if (i > 0 && (raw.length - i) % 3 == 0) {
        buffer.write(',');
      }
      buffer.write(raw[i]);
    }
    return (value < 0 ? '-' : '') + buffer.toString();
  }

  String _dateText(DateTime value) {
    final year = value.year.toString().padLeft(4, '0');
    final month = value.month.toString().padLeft(2, '0');
    final day = value.day.toString().padLeft(2, '0');
    return year + '/' + month + '/' + day;
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _documentDate,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );

    if (picked != null && mounted) {
      setState(() => _documentDate = picked);
    }
  }

  Future<void> _save({required bool queueForSync}) async {
    final lines = <LocalJournalLineInput>[];

    for (var i = 0; i < _rows.length; i++) {
      final row = _rows[i];
      final debit = _parseAmount(row.debit.text);
      final credit = _parseAmount(row.credit.text);
      final hasAmount = debit > 0 || credit > 0;

      if (!hasAmount) continue;

      if (row.accountId == null) {
        _showError(
          'برای ردیف ' +
              (i + 1).toString() +
              ' حساب انتخاب نشده است.',
        );
        return;
      }

      lines.add(
        LocalJournalLineInput(
          accountId: row.accountId!,
          description: row.description.text,
          debit: debit,
          credit: credit,
        ),
      );
    }

    setState(() => _busy = true);

    try {
      await widget.localDatabase.saveLocalJournal(
        companyId: widget.companyId,
        documentDate: _documentDate,
        description: _description.text,
        lines: lines,
        queueForSync: queueForSync,
      );

      if (!mounted) return;

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            queueForSync
                ? 'سند در SQLite ذخیره و وارد صف همگام‌سازی شد.'
                : 'پیش‌نویس سند در SQLite ذخیره شد.',
          ),
        ),
      );

      Navigator.of(context).pop();
    } on ArgumentError catch (error) {
      _showError(error.message?.toString() ?? 'اطلاعات سند معتبر نیست.');
    } catch (error) {
      _showError('ذخیره سند ناموفق بود: ' + error.toString());
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  void _showError(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(message)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final difference = _debitTotal - _creditTotal;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('ثبت سند حسابداری'),
        ),
        body: FutureBuilder<List<CachedAccount>>(
          future: _accountsFuture,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }

            if (snapshot.hasError) {
              return Center(
                child: Text(
                  'خواندن کدینگ حساب‌ها ناموفق بود: ' +
                      snapshot.error.toString(),
                ),
              );
            }

            final accounts = (snapshot.data ?? const <CachedAccount>[])
                .where((account) => account.isActive && account.isPostable)
                .toList(growable: false);

            if (accounts.isEmpty) {
              return const Center(
                child: Text(
                  'حساب قابل ثبت پیدا نشد. ابتدا کدینگ حساب‌ها را در حالت Online دریافت کنید.',
                ),
              );
            }

            return ListView(
              padding: const EdgeInsets.all(24),
              children: [
                Wrap(
                  spacing: 16,
                  runSpacing: 16,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    SizedBox(
                      width: 220,
                      child: OutlinedButton.icon(
                        onPressed: _busy ? null : _pickDate,
                        icon: const Icon(Icons.calendar_month_outlined),
                        label: Text(
                          'تاریخ سند: ' + _dateText(_documentDate),
                        ),
                      ),
                    ),
                    SizedBox(
                      width: 520,
                      child: TextField(
                        controller: _description,
                        enabled: !_busy,
                        decoration: const InputDecoration(
                          labelText: 'شرح سند',
                          prefixIcon: Icon(Icons.subject_outlined),
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 24),
                for (var index = 0; index < _rows.length; index++) ...[
                  _JournalLineCard(
                    index: index,
                    row: _rows[index],
                    accounts: accounts,
                    enabled: !_busy,
                    onChanged: () => setState(() {}),
                    onRemove: () => _removeRow(index),
                    canRemove: _rows.length > 2,
                  ),
                  const SizedBox(height: 12),
                ],
                Align(
                  alignment: Alignment.centerRight,
                  child: OutlinedButton.icon(
                    onPressed: _busy ? null : _addRow,
                    icon: const Icon(Icons.add),
                    label: const Text('افزودن ردیف'),
                  ),
                ),
                const SizedBox(height: 24),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: Wrap(
                      spacing: 28,
                      runSpacing: 12,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        Text(
                          'جمع بدهکار: ' +
                              _money(_debitTotal) +
                              ' ریال',
                        ),
                        Text(
                          'جمع بستانکار: ' +
                              _money(_creditTotal) +
                              ' ریال',
                        ),
                        Chip(
                          avatar: Icon(
                            _balanced
                                ? Icons.check_circle_outline
                                : Icons.warning_amber_outlined,
                            size: 18,
                          ),
                          label: Text(
                            _balanced
                                ? 'سند متعادل است'
                                : 'اختلاف: ' +
                                    _money(difference.abs()) +
                                    ' ریال',
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 24),
                Wrap(
                  spacing: 12,
                  runSpacing: 12,
                  children: [
                    OutlinedButton.icon(
                      onPressed:
                          _busy ? null : () => _save(queueForSync: false),
                      icon: const Icon(Icons.save_outlined),
                      label: const Padding(
                        padding: EdgeInsets.symmetric(vertical: 12),
                        child: Text('ذخیره پیش‌نویس'),
                      ),
                    ),
                    FilledButton.icon(
                      onPressed: _busy || !_balanced
                          ? null
                          : () => _save(queueForSync: true),
                      icon: _busy
                          ? const SizedBox(
                              width: 18,
                              height: 18,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                              ),
                            )
                          : const Icon(Icons.cloud_upload_outlined),
                      label: const Padding(
                        padding: EdgeInsets.symmetric(vertical: 12),
                        child: Text('ثبت و آماده همگام‌سازی'),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                const Text(
                  'واحد پایه مبلغ در این نسخه ریال است. شماره قطعی سند هنگام ثبت روی سرور تعیین خواهد شد.',
                  style: TextStyle(fontSize: 12),
                ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _JournalLineCard extends StatelessWidget {
  const _JournalLineCard({
    required this.index,
    required this.row,
    required this.accounts,
    required this.enabled,
    required this.onChanged,
    required this.onRemove,
    required this.canRemove,
  });

  final int index;
  final _JournalRowEditor row;
  final List<CachedAccount> accounts;
  final bool enabled;
  final VoidCallback onChanged;
  final VoidCallback onRemove;
  final bool canRemove;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: LayoutBuilder(
          builder: (context, constraints) {
            final compact = constraints.maxWidth < 760;

            final account = DropdownButtonFormField<String>(
              initialValue: row.accountId,
              isExpanded: true,
              decoration: InputDecoration(
                labelText: 'حساب ردیف ' + (index + 1).toString(),
              ),
              items: [
                for (final item in accounts)
                  DropdownMenuItem(
                    value: item.id,
                    child: Text(item.code + ' — ' + item.name),
                  ),
              ],
              onChanged: enabled
                  ? (value) {
                      row.accountId = value;
                      onChanged();
                    }
                  : null,
            );

            final debit = TextField(
              controller: row.debit,
              enabled: enabled,
              keyboardType: TextInputType.number,
              textDirection: TextDirection.ltr,
              onChanged: (_) => onChanged(),
              decoration: const InputDecoration(
                labelText: 'بدهکار (ریال)',
              ),
            );

            final credit = TextField(
              controller: row.credit,
              enabled: enabled,
              keyboardType: TextInputType.number,
              textDirection: TextDirection.ltr,
              onChanged: (_) => onChanged(),
              decoration: const InputDecoration(
                labelText: 'بستانکار (ریال)',
              ),
            );

            final description = TextField(
              controller: row.description,
              enabled: enabled,
              decoration: const InputDecoration(
                labelText: 'شرح ردیف',
              ),
            );

            if (compact) {
              return Column(
                children: [
                  account,
                  const SizedBox(height: 12),
                  debit,
                  const SizedBox(height: 12),
                  credit,
                  const SizedBox(height: 12),
                  description,
                  if (canRemove)
                    Align(
                      alignment: Alignment.centerLeft,
                      child: IconButton(
                        tooltip: 'حذف ردیف',
                        onPressed: enabled ? onRemove : null,
                        icon: const Icon(Icons.delete_outline),
                      ),
                    ),
                ],
              );
            }

            return Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(flex: 3, child: account),
                const SizedBox(width: 12),
                Expanded(flex: 2, child: debit),
                const SizedBox(width: 12),
                Expanded(flex: 2, child: credit),
                const SizedBox(width: 12),
                Expanded(flex: 3, child: description),
                if (canRemove)
                  IconButton(
                    tooltip: 'حذف ردیف',
                    onPressed: enabled ? onRemove : null,
                    icon: const Icon(Icons.delete_outline),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _JournalRowEditor {
  String? accountId;
  final debit = TextEditingController();
  final credit = TextEditingController();
  final description = TextEditingController();

  void dispose() {
    debit.dispose();
    credit.dispose();
    description.dispose();
  }
}
