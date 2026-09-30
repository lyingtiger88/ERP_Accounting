import '../api/api_client.dart';
import '../database/local_database.dart';

class DetailAccountPullRunResult {
  const DetailAccountPullRunResult({
    required this.pulled,
    required this.startCursor,
    required this.endCursor,
  });

  final int pulled;
  final int startCursor;
  final int endCursor;
}

class DetailAccountPullSyncService {
  const DetailAccountPullSyncService({
    required this.localDatabase,
    required this.apiClient,
  });

  final LocalDatabase localDatabase;
  final ApiClient apiClient;

  Future<DetailAccountPullRunResult> pullAll({
    required String companyId,
    required String bearerToken,
    int pageSize = 100,
  }) async {
    final startCursor =
        await localDatabase.getDetailAccountPullCursor(companyId);

    var cursor = startCursor;
    var pulled = 0;
    var pages = 0;

    while (true) {
      pages++;

      if (pages > 1000) {
        throw StateError(
          'Detail-account pull exceeded the safety page limit.',
        );
      }

      final page = await apiClient.pullDetailAccountChanges(
        bearerToken: bearerToken,
        afterCursor: cursor,
        limit: pageSize,
      );

      if (page.nextCursor < cursor) {
        throw StateError(
          'Server returned a detail cursor older than the local cursor.',
        );
      }

      await localDatabase.applyServerDetailAccountPage(
        companyId: companyId,
        page: page,
      );

      pulled += page.changes.length;

      if (!page.hasMore) {
        cursor = page.nextCursor;
        break;
      }

      if (page.nextCursor <= cursor) {
        throw StateError(
          'Detail-account cursor did not advance while more data exists.',
        );
      }

      cursor = page.nextCursor;
    }

    return DetailAccountPullRunResult(
      pulled: pulled,
      startCursor: startCursor,
      endCursor: cursor,
    );
  }
}
