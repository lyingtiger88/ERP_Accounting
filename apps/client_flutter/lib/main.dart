import 'package:flutter/material.dart';

import 'src/app.dart';
import 'src/core/api/api_client.dart';
import 'src/core/database/local_database.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final localDatabase = LocalDatabase.instance;
  await localDatabase.initialize();

  final configuredApi =
      await localDatabase.getMeta('api_base_url');
  ApiClient.configureBaseUrl(configuredApi);

  runApp(
    ErpAccountingApp(localDatabase: localDatabase),
  );
}
