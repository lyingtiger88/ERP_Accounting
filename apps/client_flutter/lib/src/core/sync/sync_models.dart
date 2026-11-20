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
    final accountId = _requiredText(json, 'accountId');
    final debit = _wholeAmount(json['debit'], 'debit');
    final credit = _wholeAmount(json['credit'], 'credit');
    if (debit < 0 || credit < 0 || (debit > 0 && credit > 0)) {
      throw const FormatException(
        'Journal line must have non-negative amounts on one side only.',
      );
    }
    final foreignDebit = _optionalAmount(json['foreignDebit'], 'foreignDebit');
    final foreignCredit = _optionalAmount(json['foreignCredit'], 'foreignCredit');
    final exchangeRate = _optionalAmount(json['exchangeRate'], 'exchangeRate');
    if ((foreignDebit ?? 0) < 0 ||
        (foreignCredit ?? 0) < 0 ||
        (exchangeRate != null && exchangeRate <= 0)) {
      throw const FormatException(
        'Foreign amounts must be non-negative and exchange rate must be positive.',
      );
    }
    return JournalPullLine(
      accountId: accountId,
      detailAccountId: json['detailAccountId'] as String?,
      costCenterId: json['costCenterId'] as String?,
      projectId: json['projectId'] as String?,
      description: json['description'] as String?,
      debit: debit,
      credit: credit,
      currencyId: json['currencyId'] as String?,
      foreignDebit: foreignDebit,
      foreignCredit: foreignCredit,
      exchangeRate: exchangeRate,
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
    final cursor = _requiredCursor(json, 'cursor');
    final documentDate = _requiredText(json, 'documentDate');
    if (DateTime.tryParse(documentDate) == null) {
      throw const FormatException('Journal documentDate must be a valid ISO date.');
    }
    final lines = _parseList(json, 'lines')
        .map((item) => JournalPullLine.fromJson(item))
        .toList(growable: false);
    if (lines.isEmpty) {
      throw const FormatException('Journal change must contain at least one line.');
    }
    return JournalPullChange(
      cursor: cursor,
      journalEntryId: _requiredText(json, 'journalEntryId'),
      number: _requiredText(json, 'number'),
      fiscalYearId: json['fiscalYearId'] as String?,
      documentDate: documentDate,
      description: json['description'] as String?,
      status: json['status'].toString(),
      postedAt: json['postedAt'] == null
          ? null
          : DateTime.parse(json['postedAt'] as String),
      reversalOfJournalEntryId:
          json['reversalOfJournalEntryId'] as String?,
      lines: lines,
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
    final nextCursor = _requiredCursor(json, 'nextCursor');
    final hasMore = json['hasMore'] as bool? ?? false;
    final changes = _parseList(json, 'changes')
        .map((item) => JournalPullChange.fromJson(item))
        .toList(growable: false);
    _validatePageCursors(
      changes.map((change) => change.cursor).toList(growable: false),
      nextCursor,
      hasMore,
      'journal',
    );
    return JournalPullPage(
      nextCursor: nextCursor,
      hasMore: hasMore,
      changes: changes,
    );
  }
}

int _wholeAmount(dynamic value, String field) {
  if (value is int) return value;

  if (value is num && value.isFinite) {
    final rounded = value.round();
    if (value.toDouble() == rounded.toDouble()) {
      return rounded;
    }
  }

  throw FormatException(
    'Server journal $field contains a fractional IRR amount.',
  );
}


String _requiredText(Map<String, dynamic> json, String field) {
  final value = json[field];
  if (value is String && value.trim().isNotEmpty) return value.trim();
  throw FormatException('Server sync response is missing required $field.');
}

int _requiredCursor(Map<String, dynamic> json, String field) {
  final value = json[field];
  if (value is! num || !value.isFinite || value < 0 || value != value.toInt()) {
    throw FormatException('Server sync $field must be a non-negative integer.');
  }
  return value.toInt();
}

num? _optionalAmount(dynamic value, String field) {
  if (value == null) return null;
  if (value is num && value.isFinite) return value;
  throw FormatException('Server sync $field must be a finite number.');
}

List<Map<String, dynamic>> _parseList(
  Map<String, dynamic> json,
  String field,
) {
  final value = json[field];
  if (value is! List) {
    throw FormatException('Server sync $field must be an array.');
  }
  return value.map((item) {
    if (item is! Map) {
      throw FormatException('Server sync $field entries must be objects.');
    }
    return Map<String, dynamic>.from(item);
  }).toList(growable: false);
}

void _validatePageCursors(
  List<int> cursors,
  int nextCursor,
  bool hasMore,
  String pageName,
) {
  if (hasMore && cursors.isEmpty) {
    throw FormatException('$pageName page cannot be empty when hasMore is true.');
  }
  var previous = -1;
  for (final cursor in cursors) {
    if (cursor <= previous) {
      throw FormatException('$pageName page cursors must be strictly increasing.');
    }
    previous = cursor;
  }
  if (cursors.isNotEmpty && nextCursor < cursors.last) {
    throw FormatException('$pageName nextCursor cannot be behind its last change.');
  }
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
    final entity = json['entity'];
    if (entity is! Map) {
      throw const FormatException('Detail-account change entity must be an object.');
    }
    final normalized = Map<String, dynamic>.from(entity);
    _requiredText(normalized, 'id');
    final revision = normalized['revision'];
    if (revision is! num ||
        !revision.isFinite ||
        revision < 1 ||
        revision != revision.toInt()) {
      throw const FormatException(
        'Detail-account revision must be a positive integer.',
      );
    }
    return DetailAccountPullChange(
      cursor: _requiredCursor(json, 'cursor'),
      entity: normalized,
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
    final nextCursor = _requiredCursor(json, 'nextCursor');
    final hasMore = json['hasMore'] as bool? ?? false;
    final changes = _parseList(json, 'changes')
        .map((item) => DetailAccountPullChange.fromJson(item))
        .toList(growable: false);
    _validatePageCursors(
      changes.map((change) => change.cursor).toList(growable: false),
      nextCursor,
      hasMore,
      'detail-account',
    );
    return DetailAccountPullPage(
      nextCursor: nextCursor,
      hasMore: hasMore,
      changes: changes,
    );
  }
}
