# C# Client-Server Application with TCP protocol, loading and parsing XML-files

## Description
The application have the following functionality:
- TCP protocol to send/recieve messages: 'TcpClient', 'TcpListener';
- Text formats (XML, JSON, etc.) are not used for data transmission over TCP: while 'binaryWriter', 'binaryReader' are used;
- Network part of the apps: 'TcpClient' are on clients sides, 'TcpListener' - on server side, - which was done in the launch in new threads;
- For each new connection 'cpListener server creates a new thread for handle connection with a new clients';
- The server is able to handle multithreading: parallel work with several clients, saving dictionary of connected clients;
- Parse XML-file is handled on the server side;
- User-friendly interface is implemented by WFP;
- The app handles main exceptions, therefore wrong actions wouldn't lead to an emergency shutdown of the app;
- Clients recieve data-message automatically after loading XML-file on the server's side, and clients have ability to request data again manually.

## Main features:
User-friendly interface by WPF, TcpClient/TcpListener, Sockets, Asynchronous, Parallel and Multithreaded programming.

![Not_connected.png](materials%2Fpictures%2FNot_connected.png)

![1_connected.png](materials%2Fpictures%2F1_connected.png)

![All_connected.png](materials%2Fpictures%2FAll_connected.png)

![Send_message.png](materials%2Fpictures%2FSend_message.png)

![Send_new_message_Request_again.png](materials%2Fpictures%2FSend_new_message_Request_again.png)
