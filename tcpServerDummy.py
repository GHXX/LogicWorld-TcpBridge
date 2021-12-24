# Symbolic name, meaning all available interfaces
VAR_HOST = ''
# Arbitrary non-privileged port
VAR_PORT = 9999

import socket
import sys

s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

local_hostname = socket.gethostname()
local_ip = socket.gethostbyname(local_hostname)

# Bind socket to local host and port
try:
    s.bind((VAR_HOST, VAR_PORT))
except socket.error as msg:
    print('Bind failed. Error Code : ' + str(msg[0]) + ' Message ' + msg[1])
    sys.exit()

# Start listening on socket
s.listen(10)
print('Socket now listening on ' + local_ip + '/' + local_hostname + ' on port ' + str(VAR_PORT))
# Now keep talking with the client
while 1:
    # Wait to accept a connection - blocking call
    conn, addr = s.accept()
    print('Connection initiated from ' + addr[0])
    while 1:
        buf = conn.recv(1024)
        if len(buf) == 0:
            print("Received buffer with no contents. Closing conn.")
            conn.close()
            print("Listening again")
            break
        else:
            print("Received: "+str(buf))
            conn.send(buf)

s.close()