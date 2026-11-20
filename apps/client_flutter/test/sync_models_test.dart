import 'package:flutter_test/flutter_test.dart';
import 'package:zarr/src/core/sync/sync_models.dart';

Map<String, dynamic> journalLine({
  dynamic debit = 100,
  dynamic credit = 0,
}) => {
  'accountId': 'account-1',
  'debit': debit,
  'credit': credit,
};

Map<String, dynamic> journalChange({
  int cursor = 1,
  List<Map<String, dynamic>>? lines,
}) => {
  'cursor': cursor,
  'journalEntryId': 'journal-1',
  'number': 'JV-001',
  'documentDate': '2026-11-20',
  'status': 'Posted',
  'lines': lines ?? [journalLine(), journalLine(debit: 0, credit: 100)],
};

Map<String, dynamic> journalPage({
  int nextCursor = 1,
  bool hasMore = false,
  List<Map<String, dynamic>>? changes,
}) => {
  'nextCursor': nextCursor,
  'hasMore': hasMore,
  'changes': changes ?? [journalChange()],
};

Map<String, dynamic> detailChange({
  int cursor = 1,
  int revision = 1,
}) => {
  'cursor': cursor,
  'entity': {
    'id': 'detail-1',
    'revision': revision,
    'code': '10001',
    'name': 'Customer',
  },
};

void main() {
  group('Journal delta parsing', () {
    test('accepts a valid page and preserves its cursor', () {
      final page = JournalPullPage.fromJson(journalPage());
      expect(page.nextCursor, 1);
      expect(page.changes.single.cursor, 1);
      expect(page.changes.single.lines, hasLength(2));
    });

    test('accepts an empty terminal page', () {
      final page = JournalPullPage.fromJson({
        'nextCursor': 10,
        'changes': <dynamic>[],
      });
      expect(page.hasMore, isFalse);
      expect(page.changes, isEmpty);
      expect(page.nextCursor, 10);
    });

    test('rejects a negative page cursor', () {
      expect(
        () => JournalPullPage.fromJson(journalPage(nextCursor: -1)),
        throwsFormatException,
      );
    });

    test('rejects a fractional page cursor', () {
      expect(
        () => JournalPullPage.fromJson({
          'nextCursor': 1.5,
          'changes': <dynamic>[],
        }),
        throwsFormatException,
      );
    });

    test('rejects an empty page that claims more data exists', () {
      expect(
        () => JournalPullPage.fromJson(journalPage(
          nextCursor: 1,
          hasMore: true,
          changes: <Map<String, dynamic>>[],
        )),
        throwsFormatException,
      );
    });

    test('rejects duplicate or descending change cursors', () {
      expect(
        () => JournalPullPage.fromJson(journalPage(
          nextCursor: 2,
          changes: [journalChange(cursor: 2), journalChange(cursor: 2)],
        )),
        throwsFormatException,
      );
      expect(
        () => JournalPullPage.fromJson(journalPage(
          nextCursor: 2,
          changes: [journalChange(cursor: 2), journalChange(cursor: 1)],
        )),
        throwsFormatException,
      );
    });

    test('rejects nextCursor behind the last change', () {
      expect(
        () => JournalPullPage.fromJson(journalPage(
          nextCursor: 1,
          changes: [journalChange(cursor: 2)],
        )),
        throwsFormatException,
      );
    });

    test('rejects a journal change without lines', () {
      expect(
        () => JournalPullPage.fromJson(journalPage(
          changes: [journalChange(lines: <Map<String, dynamic>>[])],
        )),
        throwsFormatException,
      );
    });

    test('rejects fractional IRR values', () {
      expect(
        () => JournalPullLine.fromJson(journalLine(debit: 10.25)),
        throwsFormatException,
      );
    });

    test('rejects negative and double-sided journal lines', () {
      expect(
        () => JournalPullLine.fromJson(journalLine(debit: -1)),
        throwsFormatException,
      );
      expect(
        () => JournalPullLine.fromJson(journalLine(debit: 10, credit: 10)),
        throwsFormatException,
      );
    });

    test('rejects invalid journal document dates', () {
      final change = journalChange()..['documentDate'] = 'not-a-date';
      expect(
        () => JournalPullPage.fromJson(journalPage(changes: [change])),
        throwsFormatException,
      );
    });

    test('rejects invalid foreign amount and exchange rate', () {
      expect(
        () => JournalPullLine.fromJson({
          ...journalLine(),
          'foreignDebit': double.infinity,
        }),
        throwsFormatException,
      );
      expect(
        () => JournalPullLine.fromJson({
          ...journalLine(),
          'exchangeRate': 0,
        }),
        throwsFormatException,
      );
      expect(
        () => JournalPullLine.fromJson({
          ...journalLine(),
          'foreignDebit': 10,
          'foreignCredit': 10,
          'exchangeRate': 2,
        }),
        throwsFormatException,
      );
    });

    test('rejects missing required journal identifiers', () {
      final change = journalChange()..remove('journalEntryId');
      expect(
        () => JournalPullPage.fromJson(journalPage(changes: [change])),
        throwsFormatException,
      );
    });
  });

  group('Detail-account delta parsing', () {
    test('accepts a valid entity and positive revision', () {
      final change = DetailAccountPullChange.fromJson(detailChange());
      expect(change.cursor, 1);
      expect(change.entity['id'], 'detail-1');
      expect(change.entity['revision'], 1);
    });

    test('rejects missing entity identifier', () {
      final change = detailChange();
      (change['entity'] as Map<String, dynamic>).remove('id');
      expect(
        () => DetailAccountPullChange.fromJson(change),
        throwsFormatException,
      );
    });

    test('rejects zero, negative and fractional revisions', () {
      for (final revision in [0, -1, 1.5]) {
        final payload = detailChange();
        (payload['entity'] as Map<String, dynamic>)['revision'] = revision;
        expect(
          () => DetailAccountPullChange.fromJson(payload),
          throwsFormatException,
        );
      }
    });

    test('rejects duplicate detail-account cursors', () {
      expect(
        () => DetailAccountPullPage.fromJson({
          'nextCursor': 2,
          'changes': [detailChange(cursor: 1), detailChange(cursor: 1)],
        }),
        throwsFormatException,
      );
    });

    test('rejects an empty non-terminal detail page', () {
      expect(
        () => DetailAccountPullPage.fromJson({
          'nextCursor': 2,
          'hasMore': true,
          'changes': <dynamic>[],
        }),
        throwsFormatException,
      );
    });
  });
}
