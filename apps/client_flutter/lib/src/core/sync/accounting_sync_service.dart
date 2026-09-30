import '../api/api_client.dart';
import '../database/local_database.dart';
import 'journal_pull_sync_service.dart';
import 'outbox_sync_service.dart';

class AccountingSyncRunResult {
  const AccountingSyncRunResult({
    required this.pushed,
    required this.pushFailed,
    required this.pulled,
    required this.remainingOutbox,
    required this.stoppedByNetwork,
    required this.startCursor,
    required this.endCursor,
  });

  final int pushed;
  final int pushFailed;
  final int pulled;
  final int remainingOutbox;
  final bool stoppedByNetwork;
  final int startCursor;
  final int endCursor;
}

class AccountingSyncService {
  const AccountingSyncService({
    required this.localDatabase,
    required this.apiClient,
  });

  final LocalDatabase localDatabase;
  final ApiClient apiClient;

  Future<AccountingSyncRunResult> syncAll({
    required String companyId,
    required String bearerToken,
  }) async {
    final push = await OutboxSyncService(
      localDatabase: localDatabase,
      apiClient: apiClient,
    ).syncPending(
      bearerToken: bearerToken,
    );

    final currentCursor =
        await localDatabase.getJournalPullCursor(companyId);

    if (push.stoppedByNetwork) {
      return AccountingSyncRunResult(
        pushed: push.synced,
        pushFailed: push.failed,
        pulled: 0,
        remainingOutbox: push.remaining,
        stoppedByNetwork: true,
        startCursor: currentCursor,
        endCursor: currentCursor,
      );
    }

    final pull = await JournalPullSyncService(
      localDatabase: localDatabase,
      apiClient: apiClient,
    ).pullAll(
      companyId: companyId,
      bearerToken: bearerToken,
    );

    return AccountingSyncRunResult(
      pushed: push.synced,
      pushFailed: push.failed,
      pulled: pull.pulled,
      remainingOutbox: push.remaining,
      stoppedByNetwork: false,
      startCursor: pull.startCursor,
      endCursor: pull.endCursor,
    );
  }
}
