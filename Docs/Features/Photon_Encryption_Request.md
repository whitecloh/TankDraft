# Запрос плагина шифрования Photon
Status: ответ Photon получен владельцем; плагин бесплатный, direct encryption отложено
Last reviewed: 2026-09-15

Владелец передал ответ Luke (Photon/Exit Games): DatagramEncryption plugin бесплатный и не требует дополнительной оплаты. Поддержка рекомендует добавлять direct connection encryption позднее в разработке либо после релиза, когда для него есть конкретная необходимость, и просит объяснить наш сценарий и причину раннего запроса. Это содержание предоставленного владельцем письма, а не самостоятельная проверка почтового ящика. Доступность скачивания и получение файлов плагина пока не подтверждены.

Текущий этап продолжается в согласованном закрытом QA без direct encryption; установка плагина не блокирует работу. Причина первоначального обращения — раннее следование официальной инструкции Fusion Connection Encryption и требование защиты взаимодействий игры. Это не означает, что шифрование само по себе валидирует результаты боя или покупки: авторитетная логика и проверки остаются на сервере. Ответ в поддержку после этого письма не отправлялся.

Получатель: Photon Support через официальный dashboard support form.
Отправка подтверждена видимым сообщением формы «Thanks! Your request was sent successfully.» Номер обращения форма не показала. Повторно запрос не отправлять. Бесплатность подтверждена приведённым выше ответом поддержки.
Передаваемые данные: публичный Fusion App ID, версия SDK/Unity, целевые платформы. Пароли, tickets, ключи, файлы логов не передаются.

Subject: DatagramEncryption plugin for Fusion 2.1.2 / Realtime 5.1.18

Hello Photon Support,

We are integrating Fusion 2.1.2 Stable build 2279 (Realtime 5.1.18) with Unity 6000.3.10f1 for TankDraft, a server-authoritative 1v1 game. Our Fusion App ID is 92d5f593-3b30-4493-8168-ca40ae0e55b0, currently on Free 100 CCU.

Your Fusion connection encryption documentation directs us to request the DatagramEncryption native plugin. Could you provide the compatible plugin and setup instructions for Windows x86_64 (dedicated server and PC clients), Android ARM64 clients, and Linux x86_64 servers for future deployment?

Please confirm whether the plugin is available with our current free plan and whether its use has any additional fees or licensing requirements. We do not authorize a paid upgrade.

We use PlayFab Custom Authentication, reject anonymous clients, and intend to use AuthOnceWss with DatagramEncryptionGCM on UDP port 443 plus Fusion connection encryption. The installed SDK does not include an IPhotonEncryptor implementation.

Thank you.

Источник: https://doc.photonengine.com/fusion/v2/manual/advanced/encryption
