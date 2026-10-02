class JournalPullLine {
  const JournalPullLine({
    required this.accountId,
    required this.detailAccountId,
    required this.costCenterId,
    required this.projectId,
    required this.description,
    required this.debit,
    required this.credit,
    required this.currencyId,
    required this.foreignDebit,
    required this.foreignCredit,
    required this.exchangeRate,
  });

  final String accountId;
  final String? detailAccountId;
  final String? costCenterId;
  final String? projectId;
  final String? description;
  final int debit;
  final int credit;
  final String? currencyId;
  final num? foreignDebit;
  final num? foreignCredit;
  final num? exchangeRate;

  factory JournalPullLine.fromJson(Map<String, dynamic> json) {
    return JournalPullLine(
      accountId: json['accountId'] as String,
      detailAccountId: json['detailAccountId'] as String?,
      costCenterId: json['costCenterId'] as String?,
      projectId: json['projectId'] as String?,
      description: json['description'] as String?,
      debit: _wholeAmount(json['debit'], 'debit'),
      credit: _wholeAmount(json['credit'], 'credit'),
      currencyId: json['currencyId'] as String?,
      foreignDebit: json['foreignDebit'] as num?,
      foreignCredit: json['foreignCredit'] as num?,
      exchangeRate: json['exchangeRate'] as num?,
    );
  }
}

class JournalPullChange {
  const JournalPullChange({
    required this.cursor,
    required this.journalEntryId,
    required this.number,
    required this.fiscalYearId,
    required this.documentDate,
    required this.description,
    required this.status,
    required this.postedAt,
    required this.reversalOfJournalEntryId,
    required this.lines,
  });

  final int cursor;
  final String journalEntryId;
  final String number;
  final String? fiscalYearId;
  final String documentDate;
  final String? description;
  final String status;
  final DateTime? postedAt;
  final String? reversalOfJournalEntryId;
  final List<JournalPullLine> lines;

  factory JournalPullChange.fromJson(Map<String, dynamic> json) {
    return JournalPullChange(
      cursor: (json['cursor'] as num).toInt(),
      journalEntryId: json['journalEntryId'] as String,
      number: json['number'] as String,
      fiscalYearId: json['fiscalYearId'] as String?,
      documentDate: json['documentDate'] as String,
      description: json['description'] as String?,
      status: json['status'].toString(),
      postedAt: json['postedAt'] == null
          ? null
          : DateTime.parse(json['postedAt'] as String),
      reversalOfJournalEntryId:
          json['reversalOfJournalEntryId'] as String?,
      lines: (json['lines'] as List<dynamic>)
          .map(
            (item) => JournalPullLine.fromJson(
              Map<String, dynamic>.from(item as Map),
            ),
          )
          .toList(growable: false),
    );
  }
}

class JournalPullPage {
  const JournalPullPage({
    required this.nextCursor,
    required this.hasMore,
    required this.changes,
  });

  final int nextCursor;
  final bool hasMore;
  final List<JournalPullChange> changes;

  factory JournalPullPage.fromJson(Map<String, dynamic> json) {
    return JournalPullPage(
      nextCursor: (json['nextCursor'] as num).toInt(),
      hasMore: json['hasMore'] as bool? ?? false,
      changes: (json['changes'] as List<dynamic>)
          .map(
            (item) => JournalPullChange.fromJson(
              Map<String, dynamic>.from(item as Map),
            ),
          )
          .toList(growable: false),
    );
  }
}

int _wholeAmount(dynamic value, String field) {
  if (value is int) return value;

  if (value is num) {
    final rounded = value.round();
    if (value.toDouble() == rounded.toDouble()) {
      return rounded;
    }
  }

  throw FormatException(
    'Server journal $field contains a fractional IRR amount.',
  );
}


class DetailAccountPullChange {
  const DetailAccountPullChange({
    required this.cursor,
    required this.entity,
  });

  final int cursor;
  final Map<String, dynamic> entity;

  factory DetailAccountPullChange.fromJson(
    Map<String, dynamic> json,
  ) {
    return DetailAccountPullChange(
      cursor: (json['cursor'] as num).toInt(),
      entity: Map<String, dynamic>.from(
        json['entity'] as Map,
      ),
    );
  }
}

class DetailAccountPullPage {
  const DetailAccountPullPage({
    required this.nextCursor,
    required this.hasMore,
    required this.changes,
  });

  final int nextCursor;
  final bool hasMore;
  final List<DetailAccountPullChange> changes;

  factory DetailAccountPullPage.fromJson(
    Map<String, dynamic> json,
  ) {
    return DetailAccountPullPage(
      nextCursor: (json['nextCursor'] as num).toInt(),
      hasMore: json['hasMore'] as bool? ?? false,
      changes: (json['changes'] as List<dynamic>)
          .map(
            (item) => DetailAccountPullChange.fromJson(
              Map<String, dynamic>.from(item as Map),
            ),
          )
          .toList(growable: false),
    );
  }
}
