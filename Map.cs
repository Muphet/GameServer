﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using MySql.Data.MySqlClient;
using Dijkstra;

namespace GameServer
{
    public class City
    {
        private uint id;
        private string name;
        private uint accessLevel;
        private uint leftCoordinate;
        private uint topCoordinate;
        private string icon;

        public City(uint _id, string _name, uint _accessLevel, uint _leftCoordinate, uint _topCoordinate, string _icon)
        {
            id = _id;
            name = _name;
            accessLevel = _accessLevel;
            leftCoordinate = _leftCoordinate;
            topCoordinate = _topCoordinate;
            icon = _icon;
        }
        public uint Id
        {
            get
            {
                return id;
            }
        }
        public string Name
        {
            get
            {
                return name;
            }
        }
        public uint AccessLevel
        {
            get
            {
                return accessLevel;
            }
        }
        public uint LeftCoordinate
        {
            get
            {
                return leftCoordinate;
            }
        }
        public uint TopCoordinate
        {
            get
            {
                return topCoordinate;
            }
        }
        public string Icon
        {
            get
            {
                return icon;
            }
        }
    }

    public class Spot
    {
        private uint id_loc;
        private uint id_city;
        private char type;
        private string name;
        private uint leftCoordinate;
        private uint topCoordinate;

        public Spot(uint _id_loc, uint _id_city, char _type, string _name, uint _leftCoordinate, uint _topCoordinate)
        {
            id_loc = _id_loc;
            id_city = _id_city;
            type = _type;
            name = _name;
            leftCoordinate = _leftCoordinate;
            topCoordinate = _topCoordinate;
        }
        public uint IdLoc
        {
            get
            {
                return id_loc;
            }
        }
        public uint IdCity
        {
            get
            {
                return id_city;
            }
        }
        public char Type
        {
            get
            {
                return type;
            }
        }
        public string Name
        {
            get
            {
                return name;
            }
        }
        public uint LeftCoordinate
        {
            get
            {
                return leftCoordinate;
            }
        }
        public uint TopCoordinate
        {
            get
            {
                return topCoordinate;
            }
        }
    }

    public class Map
    {
        //lista obiektów typu City przechowujących dane o miastach
        private List<City> cityData = new List<City>();

        //lista obiektów typu Spot przechowujących dane o okolicach
        private List<Spot> spotData = new List<Spot>();

        //lista połączeń pomiędzy miastami
        private List<Connection> connections = new List<Connection>();

        //obiekt obliczeniowy Dijkstry
        private Dijkstra.Dijkstra dijkstra;

        //obiekt połączenia z bazą (ustawienia globalne)
        private GlobalMySql dataBase;

        //liczba miast
        private uint citiesNumber;

        //liczba okolic
        private uint spotsNumber;

        //największy identyfikator miasta
        private uint maxId;

        public Map(GlobalMySql GlobalMySqlObject)
        {
            //zainicjalizowanie obiektu obliczeniowego Dijkstry
            dijkstra = new Dijkstra.Dijkstra();

            citiesNumber = 0;
            maxId = 0;

            //ustawienie połączenia z bazą
            dataBase = GlobalMySqlObject;
            if (dataBase.Connection.State != ConnectionState.Open)
            {
                try
                {
                    dataBase.Connection.Open();
                }
                catch
                {
                    //
                }
            }

            MySqlCommand query = dataBase.Connection.CreateCommand();

            //ustalenie najwiekszego identyfikatora miasta w bazie
            query.CommandText = "SELECT GREATEST( MAX( id_city ) , MAX( id_cityB ) ) AS max FROM `times`";
            try
            {
                maxId = uint.Parse(query.ExecuteScalar().ToString());
            }
            catch
            {
                //
            }

            //utworznie zapytania pobierającego dane o miastach
            query.CommandText = "SELECT * FROM `map_city`";

            //pobranie danych o miastach
            try
            {
                using (MySqlDataReader reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        //utworznie obiektu typu City z danymi z pojedynczego rekordu
                        City city = new City(
                            reader.GetUInt32("id"),
                            reader.GetString("name"),
                            reader.GetUInt32("accessLevel"),
                            reader.GetUInt32("leftCoordinate"),
                            reader.GetUInt32("topCoordinate"),
                            reader.GetString("icon")
                            );

                        //dodanie obiektu do listy miast
                        cityData.Add(city);

                        //zwiększenie liczby miast
                        citiesNumber++;
                    }
                }
            }
            catch
            {
                //
            }

            //utworzenie zapytania pobierającego czasy między lokacjami
            query.CommandText = "SELECT * FROM `times`";

            //pobranie danych o miastach
            try
            {
                using (MySqlDataReader reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        //utworzenie połączenia pomiędzy 
                        Link(
                            reader.GetUInt32("id_city"),
                            reader.GetUInt32("id_cityB"),
                            reader.GetInt32("weight")
                            );
                    }
                }
            }
            catch
            {
                //
            }

            //utworznie zapytania pobierającego dane o spotach
            query.CommandText = "SELECT * FROM `surroundings`";

            //pobranie danych o spotach
            try
            {
                using (MySqlDataReader reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        //utworznie obiektu typu Spot z danymi z pojedynczego rekordu
                        Spot spot = new Spot(
                            reader.GetUInt32("id_loc"),
                            reader.GetUInt32("id_city"),
                            reader.GetChar("type"),
                            reader.GetString("name"),
                            reader.GetUInt32("leftCoordinate"),
                            reader.GetUInt32("topCoordinate")
                            );

                        //dodanie obiektu do listy spotów
                        spotData.Add(spot);

                        //zwiększenie liczby miast
                        spotsNumber++;
                    }
                }
            }
            catch
            {
                //
            }
        }

        //utworzenie połączenia dwóch lokacji
        private void Link(uint locationA, uint locationB, int time)
        {
            Connection connection = new Connection(locationA, locationB, time);
            connections.Add(connection);

            //utworzenie połączenia w drugą stronę z tą samą wagą
            connection = new Connection(locationB, locationA, time);
            connections.Add(connection);
        }

        // obliczenie najkrótszej ścieżki z lokacji A do B
        public int GetTime(uint _startLocation, uint _stopLocation)
        {
            dijkstra = new Dijkstra.Dijkstra();

            foreach (Connection connection in connections)
            {
                dijkstra.Connections.Add(connection);
            }
            /*foreach (City city in cityData)
            {
                dijkstra.Locations.Add(city.Id);
            }*/
            for (uint i = 0; i <= maxId + 1; i++)
            {
                dijkstra.Locations.Add(i);
            }

            // wyliczenie najkrótszej ścieżki między lokacjami
            return dijkstra.CalculateMinCost(_startLocation, _stopLocation);
        }

        //AKCESORY
        public List<City> CityData
        {
            get
            {
                return cityData;
            }
        }
        public uint CitiesNumber
        {
            get
            {
                return citiesNumber;
            }
        }
        public List<Spot> SpotData
        {
            get
            {
                return spotData;
            }
        }
        public uint SpotsNumber
        {
            get
            {
                return spotsNumber;
            }
        }
    }
}
