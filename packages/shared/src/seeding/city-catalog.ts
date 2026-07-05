/** Real-world city anchors for Echo Site seeding (v1: US metros). */
export interface CityAnchor {
  slug: string;
  name: string;
  lat: number;
  lng: number;
  population: number;
}

export const CITY_CATALOG: CityAnchor[] = [
  { slug: 'nyc', name: 'New York', lat: 40.7128, lng: -74.006, population: 8_336_817 },
  { slug: 'la', name: 'Los Angeles', lat: 34.0522, lng: -118.2437, population: 3_898_747 },
  { slug: 'chicago', name: 'Chicago', lat: 41.8781, lng: -87.6298, population: 2_746_388 },
  { slug: 'houston', name: 'Houston', lat: 29.7604, lng: -95.3698, population: 2_304_580 },
  { slug: 'phoenix', name: 'Phoenix', lat: 33.4484, lng: -112.074, population: 1_608_139 },
  { slug: 'philadelphia', name: 'Philadelphia', lat: 39.9526, lng: -75.1652, population: 1_603_797 },
  { slug: 'san-antonio', name: 'San Antonio', lat: 29.4241, lng: -98.4936, population: 1_434_625 },
  { slug: 'san-diego', name: 'San Diego', lat: 32.7157, lng: -117.1611, population: 1_386_932 },
  { slug: 'dallas', name: 'Dallas', lat: 32.7767, lng: -96.797, population: 1_304_379 },
  { slug: 'austin', name: 'Austin', lat: 30.2672, lng: -97.7431, population: 978_908 },
  { slug: 'denver', name: 'Denver', lat: 39.7392, lng: -104.9903, population: 715_522 },
  { slug: 'seattle', name: 'Seattle', lat: 47.6062, lng: -122.3321, population: 737_015 },
  { slug: 'boston', name: 'Boston', lat: 42.3601, lng: -71.0589, population: 675_647 },
  { slug: 'nashville', name: 'Nashville', lat: 36.1627, lng: -86.7816, population: 689_447 },
  { slug: 'detroit', name: 'Detroit', lat: 42.3314, lng: -83.0458, population: 639_111 },
  { slug: 'portland', name: 'Portland', lat: 45.5152, lng: -122.6784, population: 652_503 },
  { slug: 'las-vegas', name: 'Las Vegas', lat: 36.1699, lng: -115.1398, population: 641_903 },
  { slug: 'atlanta', name: 'Atlanta', lat: 33.749, lng: -84.388, population: 498_715 },
  { slug: 'miami', name: 'Miami', lat: 25.7617, lng: -80.1918, population: 442_241 },
  { slug: 'minneapolis', name: 'Minneapolis', lat: 44.9778, lng: -93.265, population: 429_606 },
  { slug: 'tampa', name: 'Tampa', lat: 27.9506, lng: -82.4572, population: 384_959 },
  { slug: 'salt-lake-city', name: 'Salt Lake City', lat: 40.7608, lng: -111.891, population: 199_723 },
  { slug: 'boise', name: 'Boise', lat: 43.615, lng: -116.2023, population: 235_684 },
  { slug: 'albuquerque', name: 'Albuquerque', lat: 35.0844, lng: -106.6504, population: 564_559 },
  { slug: 'tucson', name: 'Tucson', lat: 32.2226, lng: -110.9747, population: 542_629 },
  { slug: 'fresno', name: 'Fresno', lat: 36.7378, lng: -119.7871, population: 542_107 },
  { slug: 'sacramento', name: 'Sacramento', lat: 38.5816, lng: -121.4944, population: 524_943 },
  { slug: 'kansas-city', name: 'Kansas City', lat: 39.0997, lng: -94.5786, population: 508_090 },
  { slug: 'omaha', name: 'Omaha', lat: 41.2565, lng: -95.9345, population: 486_051 },
  { slug: 'colorado-springs', name: 'Colorado Springs', lat: 38.8339, lng: -104.8214, population: 478_961 },
];
