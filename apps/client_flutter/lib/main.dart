import 'package:flutter/material.dart';

import 'src/app.dart';
import 'src/core/database/local_database.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final localDatabase = LocalDatabase.instance;
  await localDatabase.initialize();

  runApp(
    ErpAccountingApp(localDatabase: localDatabase),
  );
}
