import 'package:flutter/material.dart';

import '../../core/database/local_database.dart';

class CachedAccountsPage extends StatelessWidget {
  const CachedAccountsPage({
    super.key,
    required this.companyId,
    required this.localDatabase,
  });

  final String companyId;
  final LocalDatabase localDatabase;

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('کدینگ حساب‌ها'),
        ),
        body: FutureBuilder<List<CachedAccount>>(
          future: localDatabase.getCachedAccounts(companyId),
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(
                child: CircularProgressIndicator(),
              );
            }

            if (snapshot.hasError) {
              return Center(
                child: Text(
                  'خطا در خواندن دیتابیس محلی: ' +
                      snapshot.error.toString(),
                ),
              );
            }

            final accounts = snapshot.data ?? const <CachedAccount>[];

            if (accounts.isEmpty) {
              return const Center(
                child: Text(
                  'هنوز هیچ حسابی در Cache محلی ذخیره نشده است.',
                ),
              );
            }

            return ListView.separated(
              padding: const EdgeInsets.all(20),
              itemCount: accounts.length,
              separatorBuilder: (_, __) => const Divider(height: 1),
              itemBuilder: (context, index) {
                final account = accounts[index];

                return ListTile(
                  leading: CircleAvatar(
                    child: Text(
                      account.code.substring(
                        0,
                        account.code.length >= 2 ? 2 : account.code.length,
                      ),
                    ),
                  ),
                  title: Text(
                    account.code + ' — ' + account.name,
                  ),
                  subtitle: Text(
                    account.type,
                    textDirection: TextDirection.ltr,
                  ),
                  trailing: Icon(
                    account.isActive
                        ? Icons.check_circle_outline
                        : Icons.block_outlined,
                  ),
                );
              },
            );
          },
        ),
      ),
    );
  }
}
