# LogicWorld-TcpBridge
Adds a logic component that provides tcp access.


# Pin layout:
 When looking from the big side to the smaller side (so, when looking at the input side), the upper 8 pins are the datapins, where the left pin the most significant bit.
 The bottom left pin is the clock_in input, and the one immediately to the right is the clock_out input. The one pin far to the right is the enable_in pin.
 
 On the other side, the outputs are located. When looking at the output side, the MSB of the data output pins is on the right side, and the LSB on the left side.
 In the bottom row on the output side, the pins from left to right are IsConnected, TcpError and DataReady
 
 # What do the pins do?
 A rising edge on the clock_in ping reads one byte of data from the data-in pins, and stores it internally or sends it via tcp directly. A rising edge on the clock_out pin reads 1 byte from the tcp stream and sets the output-data pins accordingly.
 A rising edge on the enable pin starts a connection attempt. A falling edge resets the tcp bridge and stops the connection.
 The connected output pin is set to true iff the tcp connection has successfully been established and is currently active.
 The tcp error pin gets set when
 
 * The connect-data has not been set properly (see below)
 * The connection could not be established
 * A tcp protocol error was encountered

The data-ready pin is active iff there is data that can be read from the tcp stream. In other words, when this is set, you can read data by sending a rising edge to the clock_out pin - otherwise it doesnt do anything.

# Initial connect data
Before enabling the enable_in pin, you need to supply the following bytes of data
* (binary MSB to LSB) MRRR RRRR
  * M .. Mode: if 1, you need to supply an IP, otherwise you need to supply a hostname (which has not been tested, and may in the future be replaced with a switch for TCP vs UDP)
  * R .. Reserved: Supply 0 for now.
 
* Mode:
  * If M == 1: 4 bytes for the ip address in the format A.B.C.D, where first A is written, then B and so on
  * If M == 0: a sequence of ascii-extended letters (0-255), ending with a colon character (':'), in the order of first to last byte. e.g. `mywebsite.com:`
  
* 2 bytes for the port, where first the higher byte is written, then the lower, e.g. port 9999 would be written as 0010 01110, 0000,1111

Once this is written, you must enable the enable-pin, to start the connection. Writing additional unexpected bytes wi
