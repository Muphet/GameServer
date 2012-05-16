﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using MySql.Data.MySqlClient;
using System.Net;
using System.Net.Sockets;
using Commands;

namespace GameServer
{
    public partial class MainForm : Form
    {
        //flaga informująca czy serwer działa czy jest wyłączony
        private bool isRunning;

        //listener nasłuchujący żądań klientów
        private TcpListener server;

        //ustawienie kodera/dekodera
        private UTF8Encoding code;

        //wątek obsługujący listenera przyłączającego klientów
        private Thread listenerTh;

        //utworzenie obiektu zawierającego ustawienia i połączenie z bazą danych
        public GlobalMySql dataBase;

        //utworzenie obiektu zawierającego dane i funkcje mapy
        private Map map;

        private Skills skills;

        //obiekt ustawień aplikacji
        private Properties.Settings settings = Properties.Settings.Default;

        //delegat funkcji przyjmującej jako argument stringa
        private delegate void SetString(string str);

        //timer działający w tle odpowiedzianly za informacje o czasie serwera
        System.Windows.Forms.Timer timer;

        //obiekt przechowujący obiekty ubiegające się o dostęp do wątku
        //private Object Sync;

        public MainForm()
        {
            InitializeComponent();

            //Sync = new Object();

            //domyślnie serwer jest wyłączony
            isRunning = false;
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += new EventHandler(DisplayServerTime);
            timer.Start();

            //utworzenie gniazda serwera
            try
            {
                server = new TcpListener(IPAddress.Any, 8001);
            }
            catch (Exception ex)
            {
                AddLog("[" + GetServerDateTime() + "][Serwer]: Nie udało się utworzyć gniazda serwera. Dalsze korzystanie z aplikacji może generować błędy! Uruchom aplikację jeszcze raz.\nDebuger message:\n" + ex.ToString());
            }

            dataBase = new GlobalMySql();

            try
            {
                if (dataBase.Connection.State != ConnectionState.Open)
                {
                    dataBase.Connection.Open();
                }
                //AddLog("[" + GetServerDateTime() + "][Serwer]: Pomyślnie połączono z hostem bazy danych '" + dataBase.MySqlHost + "' do bazy '" + dataBase.MySqlBase + "'.");
            }
            catch
            {
                AddLog("[" + GetServerDateTime() + "][Serwer]: Nie udało się nawiązać połączenia z bazą danych. Aplikacja nie będzie działać poprawanie.");
            }

            //stworzenie obiektu z danymi mapy
            map = new Map(dataBase);

            skills = new Skills(dataBase);
        }

        //dodanie tekstu do okna logów synchronicznie
        private void AddLog(string log)
        {
            logs.AppendText(log + "\n");
        }

        //dodanie tekstu do okna logów asynchronicznie
        private void AddLogAsynch(string str)
        {
            Invoke
            (
                new SetString(AddLog),
                new Object[] { str }
            );
        }

        private void DisplayServerTime(object sender, EventArgs e)
        {
            czasText.Text = GetServerDateTime();
            timeStampText.Text = GetTimeStamp();
        }

        //zamiana stringa cmd na akcję i ciąg argumentów
        private string[] CommandsToArguments(string command)
        {
            string[] args = command.Split(';');
            return args;
        }

        //metoda włączająca/wyłączająca serwer

        //funkcja logowanie do serwera
        //gdy dane logowanie są poprawne, to zwraca identyfikator gracza
        //w przeciwnym wypadku zwraca 0
        private ulong Login(string login, string md5pass)
        {
            //zdefiniowanie zmiennej polecenia w obrębie obiektu połączenia connection
            MySqlCommand polecenie = dataBase.Connection.CreateCommand();
            //utworzenie zapytania
            polecenie.CommandText = "SELECT id FROM `player` WHERE `player`.`Login`='" + login + "' AND password='" + md5pass + "'";
            //StringBuilder builder = new StringBuilder();
            try
            {
                using (MySqlDataReader reader = polecenie.ExecuteReader())
                {
                    if (reader.HasRows) //jeżeli wybrało wiersze z bazy
                    {
                        while (reader.Read())
                        {
                            //MessageBox.Show("Identyfikator gracza: " + reader.GetString(0) + " Login: " + reader.GetString(2) + " Pole nr 1: " + reader.GetString(1));
                            //connectionInfo.AppendText("\n");
                            //utworzenie nowego wątku, uruchamiającego nową aplikację
                            //new Interface(reader.GetInt32(0)).Show();
                            return reader.GetUInt64("id");
                        }
                    }
                    else
                    {
                        //MessageBox.Show("Podano błędny Login lub hasło. Spróbuj jeszcze raz podając poprawne dane lub skorzystaj z opcji przypomnienia hasła.", "Nieudane logowanie", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 0;
                    }
                }
            }
            catch
            {
                return 0;
            }
            return 0;
        }

        //pobieranie danych gracza
        private string[] GetPlayerData(ulong player_id)
        {
            string[] data;

            //zdefiniowanie zmiennej polecenia w obrębie obiektu połączenia connection
            MySqlCommand polecenie = dataBase.Connection.CreateCommand();
            //utworzenie zapytania
            polecenie.CommandText = "SELECT * FROM `player` WHERE `player`.`id`='" + player_id + "'";

            try
            {
                //próba wykonanie polecenia i zapisanie jego wyniku do reader
                using (MySqlDataReader reader = polecenie.ExecuteReader())
                {
                    if (reader.HasRows) //jeżeli wybrało wiersze z bazy
                    {
                        while (reader.Read())
                        {
                            data = new string[reader.FieldCount - 1];
                            data[0] = reader.GetString("login");
                            data[1] = reader.GetString("password");
                            data[2] = reader.GetString("access");
                            data[3] = reader.GetString("email");
                            return data;
                        }
                    }
                    else
                    {
                        //MessageBox.Show("Podano błędny Login lub hasło. Spróbuj jeszcze raz podając poprawne dane lub skorzystaj z opcji przypomnienia hasła.", "Nieudane logowanie", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return null;
                    }
                }
            }
            catch
            {
                //MessageBox.Show("Nie udało się pobrać danych z bazy danych.", "Błąd pobierania danych!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return null;
        }

        //wysyłanie danych gracza
        private bool SendPlayerData(ulong playerId, Command response, Socket socket)
        {
            string[] dane = new string[3];

            dane = GetPlayerData(playerId);

            //utworzenie odpowiedzi
            response.Request(ServerCmd.PLAYER_DATA);
            response.Add(dane);
            response.Send(socket);

            AddLogAsynch("[" + GetServerDateTime() + "][ " + dane[0] + " ]: Pobrał dane gracza.");
            return true;
        }

        //nasłuchiwanie i dodawanie klientów
        private void Listen()
        {
            AddLogAsynch("[" + GetServerDateTime() + "][Serwer]: Serwer rozpoczął nasłuchiwanie.");
            //dopóki zmienna sterująca stanem działania serwera jest ustawiona na true
            //dopóki serwer nasłuchuje
            while (isRunning)
            {
                try
                {
                    //wystartuj serwer aby rozpocząć nasłuchiwanie
                    server.Start();
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString(), "Błąd startu serwera!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                try
                {
                    //sprawdzenie czy ktoś oczekuje na obsłużenie
                    if (server.Pending())
                    {
                        //utworzenie nowego wątku klienta
                        Thread klientTh = new Thread(ClientService);
                        klientTh.Priority = ThreadPriority.BelowNormal;
                        
                        //wątek kończy się wraz z zakończeniem wątku, który go wywołał
                        klientTh.IsBackground = true;
                        //uruchomienie wątku - jako argument, gniazdo do komunikacji z klientem
                        klientTh.Start(server.AcceptSocket());
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString(), "Błąd tworzenia wątku klienta!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Thread.Sleep(1); //potrzebne aby oszczędzić czasu CPU
            server.Stop();
        }

        private string CreateMySqlUpdateQuery(string[] args, ref string log)
        {
            /*
             * UAKTUALNIANIE PÓL BAZY DANYCH
             * kolejność danych: komenda, tabela, pola, wartości, pole warunku, wartość pola warunku
             */
            //tutaj będzie przechowywane zapytanie
            string UpdateQuery = "";
            //tutaj zostanie zapisany log dla serwera
            log = "Aktualizacja w tabeli '" + args[1] + "' pól: ";

            //sprawdzenie czy liczba pól i wartości są równe (przynajmniej w teorii)
            if ((args.Length - 4) % 2 != 0)
            {
                return null;
            }

            //liczba pól, które mają zostać uaktualnione
            int fields = (args.Length - 4)/2;

            //początek zapytania z zdefiniowaną tabelą
            UpdateQuery = "UPDATE `" + dataBase.MySqlBase + "`.`" + args[1] + "` SET ";

            //dodanie argumentów pole=wartość
            for (int i = 0; i < fields; i++)
            {
                log += args[i + 2] + " => " + args[i + 2 + fields] + ", ";
                UpdateQuery += "`" + args[i + 2] + "` = '" + args[i + 2 + fields] + "', ";
            }
            //usunięcie dwóch ostatnich znaków aby pozbyć się ostatniego przecinka
            UpdateQuery = UpdateQuery.Remove(UpdateQuery.Length - 2, 2);
            log = log.Remove(log.Length - 2, 2);
            //dodanie warunku gdzie pole_jednoznaczne  = wartość
            UpdateQuery += " WHERE `" + args[1] + "`.`" + args[args.Length - 2] + "` = " + args[args.Length - 1] + "";

            return UpdateQuery;
        }

        private void ExecuteQuery(object query, GlobalMySql db)
        {
            //zdefiniowanie zmiennej polecenia w obrębie obiektu połączenia connection
            MySqlCommand polecenie = db.Connection.CreateCommand();
            //utworzenie zapytania
            polecenie.CommandText = (string)query;

            polecenie.ExecuteNonQuery();
        }

        //właściwa obsługa klienta
        private void ClientService(object s)
        {
            //gniazdo klienta, którego obsługujemy
            Socket socket = s as Socket;

            //dekoder UTF-8
            code = new UTF8Encoding();

            //zdefiniowanie nazwy klienta - potem zamieniana na nazwę użytkownika
            string clientName = socket.RemoteEndPoint.ToString();

            //informacja o tym czy klientowi udało się zalogować
            bool successLog = false;
                
            //bufor do pobierania danych od klienta
            byte[] buf;
             
            //inicjalizacja wielkości paczki z żądaniem
            int packageSize = 0;

            /* -------------- INICJALIZACJA OBIEKTÓW DLA GRACZA -------------- */

            //obiekt postaci
            Character character = null;

            /* --------------------------------------------------------------- */
            
            while (socket.Connected && isRunning && IsConnected(socket))
            {
                if (socket.Available > 0)
                {
                    //jeżeli ostatnia paczka została odczytana
                    if (packageSize == 0)
                    {
                        //to ustaw bufor na 4 bajty = int32
                        buf = new byte[4];

                        //i odczytaj wielkość nowej paczki
                        socket.Receive(buf);
                        packageSize = BitConverter.ToInt32(buf, 0);
                    }
                    else
                    {
                        //jeżeli wielkość paczki jest ustalona
                        //to ustaw bufor na wielkość jej odpowiadającą
                        buf = new byte[packageSize];
                        
                        //komenda wczytana z bufora
                        string cmd = code.GetString(buf, 0, socket.Receive(buf));

                        //po wczytaniu ustaw wielkość paczki na 0 zgłaszając tym samym gotowość do przyjęcia następnej
                        packageSize = 0;
                        
                        //utworzenie obiektu komendy
                        Command response = new Command();
                        
                        //zamiana lini komendy na nazwę akcji args[0] i argumenty - reszta tablicy
                        string[] args = CommandsToArguments(cmd);
                        
                        switch (args[0])
                        {
                            /*
                             * LOGOWANIE
                             */
                            case ClientCmd.LOGIN:
                                ulong userID = Login(args[1], args[2]);
                                if (userID == 0)
                                {
                                    AddLogAsynch("[" + GetServerDateTime() + "][Klient]: Nieudana próba logowania (Login: " + args[1] + ")");
                                }
                                else
                                {
                                    AddLogAsynch("[" + GetServerDateTime() + "][ " + args[1] + " ]: Udane logowanie. ID = " + userID);
                                    clientName = args[1];
                                    successLog = true;

                                    //utworzenie obiektu postaci dla zalogowanego gracza
                                    character = new Character(userID, dataBase);

                                    //uaktualnienie w bazie danych daty ostatniego logowania
                                    ExecuteQuery("UPDATE `" + dataBase.MySqlBase + "`.`player` SET `lastlogin` = '" + GetServerDateTime() + "' WHERE `player`.`id` =" + userID + ";", dataBase);
                                }
                                //utworzenie odpowiedzi
                                response.Request(ClientCmd.LOGIN);
                                response.Add(userID.ToString());
                                response.Add(DateTime.UtcNow.Ticks.ToString());
                                response.Send(socket);
                                break;

                            /*
                             * WYSYŁANIE DANYCH GRACZA
                             * kolejność danych: komenda, Login, hasło, dostęp, email
                             */
                            case ClientCmd.GET_PLAYER_DATA:
                                Thread sendPlayerDataTh = new Thread(unused => SendPlayerData(ulong.Parse(args[1]), response, socket));
                                sendPlayerDataTh.Priority = ThreadPriority.BelowNormal;
                                sendPlayerDataTh.IsBackground = true;
                                sendPlayerDataTh.Start();
                                break;

                            /* 
                             * WYSŁANIE DANYCH POSTACI
                             * kolejność danych: komenda, imie, poziom, doświadczenie, złoto, siła, wytrzymałość, zręczność, szczęście
                             */
                            case ClientCmd.GET_CHARACTER_DATA:
                                response.Request(ServerCmd.CHARACTER_DATA);
                                response.Add(character.Name);
                                response.Add(character.Level.ToString());
                                response.Add(character.Experience.ToString());
                                response.Add(character.Gold.ToString());
                                response.Add(character.Strength.ToString());
                                response.Add(character.Stamina.ToString());
                                response.Add(character.Dexterity.ToString());
                                response.Add(character.Luck.ToString());
                                response.Add(character.Status);
                                response.Add(character.LastDamage.ToString());
                                response.Add(character.Damage.ToString());
                                response.Add(character.LastFatigue.ToString());
                                response.Add(character.Fatigue.ToString());
                                response.Add(character.Location.ToString());
                                response.Add(character.TravelEndTime.ToString());
                                response.Add(character.TravelDestination.ToString());

                                response.Send(socket);

                                break;
                            /*
                             * UAKTUALNIANIE PÓL BAZY DANYCH
                             * kolejność danych: komenda, tabela, pola, wartości, pole warunku, wartość pola warunku
                             */
                            case ClientCmd.UPDATE_DATA_BASE:
                                string log = "";
                                string UpdateQuery = CreateMySqlUpdateQuery(args, ref log);
                                AddLogAsynch("[" + GetServerDateTime() + "][ " + clientName + " ]: " + log);
                                ExecuteQuery(UpdateQuery, dataBase);
                                break;
                            case ClientCmd.GET_CHARACTER_EQUIPMENT:
                                response.Request(ServerCmd.CHARACTER_EQUIPMENT);
                                response.Add(character.Equipment.Head.ToString());
                                response.Add(character.Equipment.Chest.ToString());
                                response.Add(character.Equipment.Legs.ToString());
                                response.Add(character.Equipment.Weapon.ToString());
                                response.Add(character.Equipment.Shield.ToString());
                                response.Send(socket);
                                break;
                            case ClientCmd.GET_CITIES:
                                response.Request(ServerCmd.CITIES);
                                response.Add(map.CitiesNumber.ToString());
                                foreach (City city in map.CityData)
                                {
                                    response.Add(city.Id.ToString());
                                    response.Add(city.Name);
                                    response.Add(city.AccessLevel.ToString());
                                    response.Add(city.LeftCoordinate.ToString());
                                    response.Add(city.TopCoordinate.ToString());
                                    response.Add(city.Icon);
                                }
                                response.Send(socket);
                                break;
                            case ClientCmd.GET_SKILLS:
                                response.Request(ServerCmd.SKILLS);
                                //AddLogAsynch("Pobiernie umiejętności");
                                foreach (Skill skill in skills.SkillList)
                                {
                                    response.Add(skill.Id.ToString());
                                    response.Add(skill.AccessLevel.ToString());
                                    response.Add(skill.Strength.ToString());
                                    response.Add(skill.Stamina.ToString());
                                    response.Add(skill.Dexterity.ToString());
                                    response.Add(skill.Luck.ToString());
                                }
                                response.Send(socket);
                                break;
                            case ClientCmd.GET_SHORTEST_PATH:
                                response.Request(ServerCmd.SHORTEST_PATH);
                                response.Add(map.GetTime(uint.Parse(args[1]), uint.Parse(args[2])).ToString());
                                response.Send(socket);
                                break;
                            case ClientCmd.GET_ENEMIES:
                                //odpyta bazke o przeciwnikow dla zadanego id okolicy
                                uint surr = uint.Parse(args[1]);
                                Enemies en = new Enemies(surr, dataBase);

                                response.Request(ServerCmd.ENEMIES);
                                response.Add(en.MobsCount.ToString());

                                foreach (Mob mb in en.EnemiesList)
                                {
                                    response.Add(mb.Id.ToString());
                                    response.Add(mb.Name.ToString());
                                    response.Add(mb.Level.ToString());
                                    response.Add(mb.BonusHP.ToString());
                                    response.Add(mb.Strength.ToString());
                                    response.Add(mb.Luck.ToString());
                                    response.Add(mb.Dexterity.ToString());
                                    response.Add(mb.Stamina.ToString());
                                    response.Add(mb.GoldDrop.ToString());
                                }
                                response.Send(socket);
                                break;
                            default:
                                AddLogAsynch("[" + GetServerDateTime() + "][Klient]: Odebrano nieznaną komendę!");
                                break;
                        }
                        response.Clear();
                    }
                }
            }
            if (successLog)
            {
                AddLogAsynch("[" + GetServerDateTime() + "][ " + clientName + " ]: Gracz rozłączył się z serwerem.");
            }
        }

        //sprawdzenie czy na gnieździe nasłuchuje jeszcze klient
        private bool IsConnected(Socket socket)
        {
            try
            {
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException) { return false; }
        }

        private void ustawieniaToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SettingsForm ustawienia = new SettingsForm(this);
            ustawienia.ShowDialog(this);
        }

        //pobranie czasu serwera w formacie akceptowanym przez bazę danych MySql
        private string GetServerDateTime()
        {
            //utworzenie obiektu DateTime dla ustalenia czasu serwera
            //zostanie on ustawiony jako czas ostatniego logowania
            //będzie dodawany do logów na serwerze
            DateTime date = DateTime.Now;

            return String.Format("{0: yyyy'-'MM'-'dd HH:mm:ss}", date);
        }

        //pobieranie czasu serwera w formacie UNIXowym w sekundach od początku ery UNIXa
        private string GetTimeStamp()
        {
            //Find unix timestamp (seconds since 01/01/1970)
            long ticks = DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks;
            ticks /= 10000000; //Convert windows ticks to seconds
            return ticks.ToString();
        }

        private void onoffToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (isRunning) //jeżeli działa
            {
                //to przy wyłączaniu ustaw przycisk do włączania
                onoffToolStripMenuItem.Text = "Włącz";

                //ustaw, że serwer jest nieaktywny
                isRunning = false;

                //wstrzymanie wątku głównego do czasu zakończenia listenerTh i włączenie go do głównego
                listenerTh.Join();

                AddLog("[" + GetServerDateTime() + "][Serwer]: Serwer zakończył nasłuchiwanie.");
            }
            else //jeżeli serwer nie jest w trakcie działania
            {
                //to podczas uruchamiania ustaw przycisk do wyłączania
                onoffToolStripMenuItem.Text = "Wyłącz";

                dataBase.RefreshConnection();

                try
                {
                    if (dataBase.Connection.State != ConnectionState.Open)
                    {
                        dataBase.Connection.Open();
                    }
                    AddLog("[" + GetServerDateTime() + "][Serwer]: Pomyślnie połączono z hostem bazy danych '" + dataBase.MySqlHost + "' do bazy '" + dataBase.MySqlBase + "'.");
                }
                catch
                {
                    AddLog("[" + GetServerDateTime() + "][Serwer]: Nie udało się nawiązać połączenia z bazą danych. Aplikacja nie będzie działać poprawanie.");
                }

                //ustaw, że serwer jest aktywny
                isRunning = true;

                //ustawienie i uruchomienie nasłuchiwania Listen() w nowym wątku
                listenerTh = new Thread(Listen);
                listenerTh.Priority = ThreadPriority.BelowNormal;
                listenerTh.IsBackground = true;
                listenerTh.Start();
            }
        }
    }
}
