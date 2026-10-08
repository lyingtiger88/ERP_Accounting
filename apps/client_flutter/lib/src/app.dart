import 'package:flutter/material.dart';

import 'core/api/api_client.dart';
import 'core/auth/secure_session_store.dart';
import 'core/database/local_database.dart';
import 'features/auth/login_page.dart';
import 'features/dashboard/dashboard_page.dart';

class ErpAccountingApp extends StatelessWidget {
  const ErpAccountingApp({
    super.key,
    required this.localDatabase,
  });

  final LocalDatabase localDatabase;

  @override
  Widget build(BuildContext context) {
    final scheme = ColorScheme.fromSeed(
      seedColor: const Color(0xFF2563EB),
      brightness: Brightness.light,
    );

    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'ERP Accounting',
      theme: ThemeData(
        colorScheme: scheme,
        useMaterial3: true,
        scaffoldBackgroundColor: const Color(0xFFF5F7FB),
        inputDecorationTheme: const InputDecorationTheme(
          border: OutlineInputBorder(),
        ),
        cardTheme: const CardThemeData(
          elevation: 0,
          margin: EdgeInsets.zero,
        ),
      ),
      home: _SessionGate(localDatabase: localDatabase),
    );
  }
}

class _SessionGate extends StatefulWidget {
  const _SessionGate({
    required this.localDatabase,
  });

  final LocalDatabase localDatabase;

  @override
  State<_SessionGate> createState() => _SessionGateState();
}

class _SessionGateState extends State<_SessionGate> {
  late Future<_RestoredSession?> _future;

  @override
  void initState() {
    super.initState();
    _future = _restore();
  }

  Future<_RestoredSession?> _restore() async {
    SecureSession? stored;
    try {
      stored = await SecureSessionStore.instance.read();
    } catch (_) {
      return null;
    }

    if (stored == null) {
      return null;
    }

    if (stored.refreshExpiresAt.isBefore(DateTime.now().toUtc())) {
      await SecureSessionStore.instance.clear();
      return null;
    }

    final api = ApiClient();

    try {
      final result = await api.refreshSession(
        refreshToken: stored.refreshToken,
      );

      if (result == null || result.mfaRequired) {
        await SecureSessionStore.instance.clear();
        return null;
      }

      await widget.localDatabase.cacheUserProfile(
        userId: result.userId,
        companyId: result.companyId,
        displayName: result.displayName,
        role: result.role,
      );

      var accountsSynced = false;

      try {
        final accounts = await api.getAccounts(
          bearerToken: result.accessToken,
        );
        final fiscalYears = await api.getFiscalYears(
          bearerToken: result.accessToken,
        );
        final details = await api.getDetailAccounts(
          bearerToken: result.accessToken,
        );

        await widget.localDatabase.replaceAccounts(
          companyId: result.companyId,
          accounts: accounts,
        );
        await widget.localDatabase.replaceFiscalYears(
          companyId: result.companyId,
          fiscalYears: fiscalYears,
        );
        await widget.localDatabase.replaceDetailAccounts(
          companyId: result.companyId,
          details: details,
        );

        accountsSynced = true;
      } on ApiException {
        // A valid restored session remains usable with the local cache.
      }

      return _RestoredSession(
        accessToken: result.accessToken,
        companyId: result.companyId,
        displayName: result.displayName,
        role: result.role,
        accountsSynced: accountsSynced,
      );
    } on ApiException {
      return null;
    }
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<_RestoredSession?>(
      future: _future,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Scaffold(
            body: Center(
              child: CircularProgressIndicator(),
            ),
          );
        }

        final session = snapshot.data;
        if (session == null) {
          return LoginPage(localDatabase: widget.localDatabase);
        }

        return DashboardPage(
          displayName: session.displayName,
          role: session.role,
          companyId: session.companyId,
          accessToken: session.accessToken,
          localDatabase: widget.localDatabase,
          accountsSynced: session.accountsSynced,
        );
      },
    );
  }
}

class _RestoredSession {
  const _RestoredSession({
    required this.accessToken,
    required this.companyId,
    required this.displayName,
    required this.role,
    required this.accountsSynced,
  });

  final String accessToken;
  final String companyId;
  final String displayName;
  final String role;
  final bool accountsSynced;
}
