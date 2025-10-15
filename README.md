# LogicWorld-TcpBridge
Adds a logic component that provides tcp access.


# Peg layout:
 When looking from the big side to the smaller side (so, when looking at the input side), the upper 8 pegs are the *data pegs*, where the left peg the most significant bit.
 The bottom left peg is the *clock_in* input, and the one immediately to the right is the *clock_out* input. The one peg far to the right is the *enable_in* peg.
 
 On the other side, the outputs are located. When looking at the output side, the MSB of the data output pegs is on the right side, and the LSB on the left side.
 In the bottom row on the output side, the pegs from left to right are *is_connected*, *tcp_error* and *data_ready*.
 
 # What do the pegs do?
 While *clock_in* is true, the Tcpbridge reads one byte of data from the data-in pegs, and stores it internally or sends it via tcp directly. (If you power that clock_in peg for more that one tick, more data will be read from the data pegs)
 While *clock_out* is true, one byte from the tcp stream is read and written to the output data pegs.
 A rising edge on the *enable_in* peg starts a connection attempt. A falling edge resets the tcp bridge and stops the connection.

 The *is_connected* output peg is set to true iff the tcp connection has successfully been established and is currently active.
 The *tcp_error* peg gets set when
 
 * The connect-data has not been set properly (see below)
 * The connection could not be established
 * A tcp protocol error was encountered

The data-ready peg is active iff there is data that can be read from the tcp stream. In other words, when this is set, you can read data by sending a rising edge to the clock_out peg - otherwise it doesnt do anything.

# Initial connect data
Before enabling the enable_in peg, you need to supply the following bytes of data
* (binary MSB to LSB) MRRR RRRR
  * M .. Mode: if 1, you need to supply an IP, otherwise you need to supply a hostname (which has not been tested, and may in the future be replaced with a switch for TCP vs UDP)
  * R .. Reserved: Supply 0 for now.
 
* Destination:
  * If M == 1: 4 bytes for the ip address in the format A.B.C.D, where first A is written, then B and so on
  * If M == 0: a sequence of ascii-extended letters (0-255), ending with a colon character (':'), in the order of first to last byte. e.g. `mywebsite.com:`
  
* 2 bytes for the port, where first the higher byte is written, then the lower, e.g. port 9999 would be written as 0010 0111, 0000 1111

Once this is written, you must enable the enable-peg, to start the connection. Writing additional unexpected bytes will cause the connection procedure to fail.

# About security and accessing local networks
For security reasons, access to the local network (localhost and home-lans) is **blocked** by default. 
I.e. if you are running a server, allowing access to other local stuff could pose a high security risk as this **will** allow people to bypass the server's firewall - therefore it is disabled by default.

If you do want to access local networks, you need to head over to the file Config.cs at TcpBridge/mod/src/server/Config.cs and prefix the IP ranges that you are trying to access with C#'s comment indicator, being `//`.

For example, the line `"127.0.0.0/8",` would become `// "127.0.0.0/8",`. 

If you are commenting out the last entry of that blacklist array, you may have to remove the comma of the last valid entry in case you are facing a compile-error.
