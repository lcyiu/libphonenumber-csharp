/*
* Copyright (C) 2011 The Libphonenumber Authors
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PhoneNumbers
{
    /*
    * Utility for international short phone numbers, such as short codes and emergency numbers. Note
    * most commercial short numbers are not handled here, but by the PhoneNumberUtil.
    *
    * @author Shaopeng Jia
    */
    public class ShortNumberUtil
    {
        private static PhoneNumberUtil phoneUtil;

        // A mapping from a country calling code to the region codes which denote the region represented
        // by that country calling code. In the case of multiple regions sharing a calling code, such as
        // the NANPA regions, the one indicated with "isMainCountryForCode" in the metadata should be
        // first.
        private static Dictionary<int, List<string>> countryCallingCodeToRegionCodeMap;
        internal const string MetaDataFilePrefix = "ShortNumberMetaData.xml";

        public ShortNumberUtil()
        {
            phoneUtil = PhoneNumberUtil.GetInstance();
            countryCallingCodeToRegionCodeMap =
                BuildMetadataFromXml.GetCountryCodeToRegionCodeMap(MetaDataFilePrefix);
        }

        // @VisibleForTesting
        public ShortNumberUtil(PhoneNumberUtil util)
        {
            phoneUtil = util;
            countryCallingCodeToRegionCodeMap =
                BuildMetadataFromXml.GetCountryCodeToRegionCodeMap(MetaDataFilePrefix);
        }

        /**
        * Returns a list with the region codes that match the specific country calling code. For
        * non-geographical country calling codes, the region code 001 is returned. Also, in the case
        * of no region code being found, an empty list is returned.
        */
        private List<string> GetRegionCodesForCountryCode(int countryCallingCode)
        {
            countryCallingCodeToRegionCodeMap.TryGetValue(countryCallingCode, out List<string> regions);
            return regions ?? new List<string>();
        }

        /**
        * Helper method to check that the country calling code of the number matches the region it's
        * being dialed from.
        */
        private bool RegionDialingFromMatchesNumber(PhoneNumber number,
            string regionDialingFrom)
        {
            List<string> regionCodes = GetRegionCodesForCountryCode(number.CountryCode);
            return regionCodes.Contains(regionDialingFrom);
        }

        /**
        * Check whether a short number is a possible number when dialed from the given region. This
        * provides a more lenient check than {@link #isValidShortNumberForRegion}.
        *
        * @param number the short number to check
        * @param regionDialingFrom the region from which the number is dialed
        * @return whether the number is a possible short number
        */
        public bool IsPossibleShortNumberForRegion(PhoneNumber number, String regionDialingFrom)
        {
            if (!RegionDialingFromMatchesNumber(number, regionDialingFrom))
            {
                return false;
            }
            PhoneMetadata phoneMetadata =
                MetadataManager.GetShortNumberMetadataForRegion(regionDialingFrom);
            if (phoneMetadata == null)
            {
                return false;
            }
            int numberLength = GetNationalSignificantNumber(number).Length;
            return phoneMetadata.GeneralDesc.PossibleLengthList.Contains(numberLength);
        }

        /**
        * Check whether a short number is a possible number. If a country calling code is shared by
        * multiple regions, this returns true if it's possible in any of them. This provides a more
        * lenient check than {@link #isValidShortNumber}. See {@link
        * #isPossibleShortNumberForRegion(PhoneNumber, String)} for details.
        *
        * @param number the short number to check
        * @return whether the number is a possible short number
        */
        public bool IsPossibleShortNumber(PhoneNumber number)
        {
            List<string> regionCodes = GetRegionCodesForCountryCode(number.CountryCode);
            int shortNumberLength = GetNationalSignificantNumber(number).Length;
            foreach (string region in regionCodes)
            {
                PhoneMetadata phoneMetadata = MetadataManager.GetShortNumberMetadataForRegion(region);
                if (phoneMetadata == null)
                {
                    continue;
                }
                if (phoneMetadata.GeneralDesc.PossibleLengthList.Contains(shortNumberLength))
                {
                    return true;
                }
            }
            return false;
        }

        /**
        * Tests whether a short number matches a valid pattern in a region. Note that this doesn't verify
        * the number is actually in use, which is impossible to tell by just looking at the number
        * itself.
        *
        * @param number the short number for which we want to test the validity
        * @param regionDialingFrom the region from which the number is dialed
        * @return whether the short number matches a valid pattern
        */
        public bool isValidShortNumberForRegion(PhoneNumber number, string regionDialingFrom)
        {
            if (!RegionDialingFromMatchesNumber(number, regionDialingFrom))
            {
                return false;
            }
            PhoneMetadata phoneMetadata =
                MetadataManager.GetShortNumberMetadataForRegion(regionDialingFrom);
            if (phoneMetadata == null)
            {
                return false;
            }
            string shortNumber = GetNationalSignificantNumber(number);
            PhoneNumberDesc generalDesc = phoneMetadata.GeneralDesc;
            if (!MatchesPossibleNumberAndNationalNumber(shortNumber, generalDesc))
            {
                return false;
            }
            PhoneNumberDesc shortNumberDesc = phoneMetadata.ShortCode;
            return MatchesPossibleNumberAndNationalNumber(shortNumber, shortNumberDesc);
        }

        /**
        * Tests whether a short number matches a valid pattern. If a country calling code is shared by
        * multiple regions, this returns true if it's valid in any of them. Note that this doesn't verify
        * the number is actually in use, which is impossible to tell by just looking at the number
        * itself. See {@link #isValidShortNumberForRegion(PhoneNumber, String)} for details.
        *
        * @param number the short number for which we want to test the validity
        * @return whether the short number matches a valid pattern
        */
        public bool IsValidShortNumber(PhoneNumber number)
        {
            List<string> regionCodes = GetRegionCodesForCountryCode(number.CountryCode);
            string regionCode = GetRegionCodeForShortNumberFromRegionList(number, regionCodes);
            if (regionCodes.Count > 1 && regionCode != null)
            {
                // If a matching region had been found for the phone number from among two or more regions,
                // then we have already implicitly verified its validity for that region.
                return true;
            }
            return isValidShortNumberForRegion(number, regionCode);
        }

        // Helper method to get the region code for a given phone number, from a list of possible region
        // codes. If the list contains more than one region, the first region for which the number is
        // valid is returned.
        private string GetRegionCodeForShortNumberFromRegionList(PhoneNumber number,
            List<string> regionCodes)
        {
            if (regionCodes.Count == 0)
            {
                return null;
            }

            if (regionCodes.Count == 1)
            {
                return regionCodes.First();
            }
            string nationalNumber = GetNationalSignificantNumber(number);
            foreach (string regionCode in regionCodes)
            {
                PhoneMetadata phoneMetadata = MetadataManager.GetShortNumberMetadataForRegion(regionCode);
                if (phoneMetadata != null
                    && MatchesPossibleNumberAndNationalNumber(nationalNumber, phoneMetadata.ShortCode))
                {
                    // The number is valid for this region.
                    return regionCode;
                }
            }
            return null;
        }

        /**
        * Returns true if the number might be used to connect to an emergency service in the given
        * region.
        *
        * This method takes into account cases where the number might contain formatting, or might have
        * additional digits appended (when it is okay to do that in the region specified).
        *
        * @param number  the phone number to test
        * @param regionCode  the region where the phone number is being dialed
        * @return  if the number might be used to connect to an emergency service in the given region.
        */
        public bool ConnectsToEmergencyNumber(String number, String regionCode)
        {
            return MatchesEmergencyNumberHelper(number, regionCode, true /* allows prefix match */);
        }

        /**
        * Returns true if the number exactly matches an emergency service number in the given region.
        *
        * This method takes into account cases where the number might contain formatting, but doesn't
        * allow additional digits to be appended.
        *
        * @param number  the phone number to test
        * @param regionCode  the region where the phone number is being dialed
        * @return  if the number exactly matches an emergency services number in the given region.
        */
        public bool IsEmergencyNumber(String number, String regionCode)
        {
            return MatchesEmergencyNumberHelper(number, regionCode, false /* doesn't allow prefix match */);
        }

        private bool MatchesEmergencyNumberHelper(String number, String regionCode,
            bool allowPrefixMatch)
        {
            number = PhoneNumberUtil.ExtractPossibleNumber(number);
            if (PhoneNumberUtil.PlusCharsPattern.MatchBeginning(number).Success)
            {
                // Returns false if the number starts with a plus sign. We don't believe dialing the country
                // code before emergency numbers (e.g. +1911) works, but later, if that proves to work, we can
                // add additional logic here to handle it.
                return false;
            }
            var metadata = phoneUtil.GetMetadataForRegion(regionCode);
            if (metadata == null || !metadata.HasEmergency)
            {
                return false;
            }
            var emergencyNumberPattern =
                new PhoneRegex(metadata.Emergency.NationalNumberPattern);
            var normalizedNumber = PhoneNumberUtil.NormalizeDigitsOnly(number);
            // In Brazil, it is impossible to append additional digits to an emergency number to dial the
            // number.
            return (!allowPrefixMatch || regionCode.Equals("BR"))
                ? emergencyNumberPattern.MatchAll(normalizedNumber).Success
                : emergencyNumberPattern.MatchBeginning(normalizedNumber).Success;

        }

        /**
        * Gets the national significant number of the a phone number. Note a national significant number
        * doesn't contain a national prefix or any formatting.
        * <p>
        * This is a temporary duplicate of the {@code getNationalSignificantNumber} method from
        * {@code PhoneNumberUtil}. Ultimately a canonical static version should exist in a separate
        * utility class (to prevent {@code ShortNumberInfo} needing to depend on PhoneNumberUtil).
        *
        * @param number  the phone number for which the national significant number is needed
        * @return  the national significant number of the PhoneNumber object passed in
        */
        private static string GetNationalSignificantNumber(PhoneNumber number)
        {
            // If a leading zero has been set, we prefix this now. Note this is not a national prefix.
            var nationalNumber = new StringBuilder(number.ItalianLeadingZero ? "0" : "");
            nationalNumber.Append(number.NationalNumber);
            return nationalNumber.ToString();
        }

        // TODO: Once we have benchmarked ShortNumberInfo, consider if it is worth keeping
        // this performance optimization.
        private bool MatchesPossibleNumberAndNationalNumber(String number,
            PhoneNumberDesc numberDesc)
        {
            if (numberDesc.PossibleLengthCount > 0
                && !numberDesc.PossibleLengthList.Contains(number.Length))
            {
                return false;
            }
            return matcherApi.matchNationalNumber(number, numberDesc, false);
        }
    }
}
