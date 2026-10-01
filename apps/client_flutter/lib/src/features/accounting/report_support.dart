import 'package:flutter/material.dart';

import '../../core/database/local_database.dart';
import '../../core/date/jalali_date.dart';

class AccountingReportPeriod {
  const AccountingReportPeriod({
    required this.fiscalYears,
    required this.selectedFiscalYearId,
    required this.from,
    required this.to,
  });

  final List<CachedFiscalYear> fiscalYears;
  final String? selectedFiscalYearId;
  final DateTime? from;
  final DateTime? to;

  static Future<AccountingReportPeriod> load({
    required LocalDatabase localDatabase,
    required String companyId,
  }) async {
    final fiscalYears =
        await localDatabase.getCachedFiscalYears(companyId);

    CachedFiscalYear? selected;

    for (final year in fiscalYears) {
      if (year.isDefault && !year.isClosed) {
        selected = year;
        break;
      }
    }

    selected ??= fiscalYears
        .where((year) => !year.isClosed)
        .cast<CachedFiscalYear?>()
        .firstOrNull;

    selected ??= fiscalYears.cast<CachedFiscalYear?>().firstOrNull;

    return AccountingReportPeriod(
      fiscalYears: fiscalYears,
      selectedFiscalYearId: selected?.id,
      from: selected == null
          ? null
          : DateTime.parse(selected.startDate),
      to: selected == null
          ? null
          : DateTime.parse(selected.endDate),
    );
  }

  AccountingReportPeriod selectFiscalYear(String? id) {
    CachedFiscalYear? selected;

    for (final year in fiscalYears) {
      if (year.id == id) {
        selected = year;
        break;
      }
    }

    return AccountingReportPeriod(
      fiscalYears: fiscalYears,
      selectedFiscalYearId: selected?.id,
      from: selected == null
          ? from
          : DateTime.parse(selected.startDate),
      to: selected == null
          ? to
          : DateTime.parse(selected.endDate),
    );
  }

  AccountingReportPeriod copyWith({
    String? selectedFiscalYearId,
    DateTime? from,
    DateTime? to,
  }) {
    return AccountingReportPeriod(
      fiscalYears: fiscalYears,
      selectedFiscalYearId:
          selectedFiscalYearId ?? this.selectedFiscalYearId,
      from: from ?? this.from,
      to: to ?? this.to,
    );
  }
}

class AccountingReportFilterBar extends StatelessWidget {
  const AccountingReportFilterBar({
    super.key,
    required this.period,
    required this.loading,
    required this.onFiscalYearChanged,
    required this.onPickFrom,
    required this.onPickTo,
    required this.onRefresh,
  });

  final AccountingReportPeriod period;
  final bool loading;
  final ValueChanged<String?> onFiscalYearChanged;
  final VoidCallback onPickFrom;
  final VoidCallback onPickTo;
  final VoidCallback onRefresh;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Wrap(
          spacing: 12,
          runSpacing: 12,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 240,
              child: DropdownButtonFormField<String>(
                initialValue: period.selectedFiscalYearId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'سال مالی',
                  prefixIcon: Icon(Icons.calendar_month_outlined),
                ),
                items: [
                  for (final year in period.fiscalYears)
                    DropdownMenuItem(
                      value: year.id,
                      child: Text(
                        year.name +
                            (year.isClosed ? ' (بسته)' : ''),
                      ),
                    ),
                ],
                onChanged: loading ? null : onFiscalYearChanged,
              ),
            ),
            OutlinedButton.icon(
              onPressed: loading ? null : onPickFrom,
              icon: const Icon(Icons.first_page_outlined),
              label: Text(
                'از: ' + formatReportDate(period.from),
              ),
            ),
            OutlinedButton.icon(
              onPressed: loading ? null : onPickTo,
              icon: const Icon(Icons.last_page_outlined),
              label: Text(
                'تا: ' + formatReportDate(period.to),
              ),
            ),
            FilledButton.icon(
              onPressed: loading ? null : onRefresh,
              icon: loading
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                      ),
                    )
                  : const Icon(Icons.refresh),
              label: const Text('به‌روزرسانی گزارش'),
            ),
          ],
        ),
      ),
    );
  }
}

String formatReportDate(DateTime? value) {
  if (value == null) return 'همه';
  return JalaliDate.fromGregorian(value).formatted;
}

String formatReportMoney(num value) {
  final isWhole = value.toDouble() == value.roundToDouble();
  final raw = isWhole
      ? value.toInt().abs().toString()
      : value.abs().toStringAsFixed(2);

  final parts = raw.split('.');
  final integer = parts.first;
  final buffer = StringBuffer();

  for (var index = 0; index < integer.length; index++) {
    if (index > 0 && (integer.length - index) % 3 == 0) {
      buffer.write(',');
    }
    buffer.write(integer[index]);
  }

  final formatted = parts.length == 1
      ? buffer.toString()
      : buffer.toString() + '.' + parts[1];

  return value < 0 ? '-' + formatted : formatted;
}

num reportNumber(dynamic value) {
  if (value is num) return value;
  return num.tryParse(value?.toString() ?? '') ?? 0;
}

extension _FirstOrNullReport<T> on Iterable<T> {
  T? get firstOrNull {
    final iterator = this.iterator;
    if (!iterator.moveNext()) return null;
    return iterator.current;
  }
}
